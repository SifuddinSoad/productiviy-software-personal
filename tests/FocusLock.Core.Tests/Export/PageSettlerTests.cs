using FocusLock.Core.Board;
using FocusLock.Core.Export;
using FocusLock.Core.Models;

namespace FocusLock.Core.Tests.Export;

public class PageSettlerTests
{
    const double Gap = PageLayout.Gap;

    /// <summary>A section whose box is <paramref name="h"/> points tall at 300 points wide.</summary>
    static ExtractItem Section(string id, string page, double y, double h, double x = 18, double w = 300) =>
        new() { Id = id, PlanId = "p", W = w, H = h * w / w, PageId = page, PageX = x, PageY = y, PageW = w };

    static TextItem Text(string id, string page, double y, double x = 18, double w = 300) =>
        new() { Id = id, PageId = page, PageX = x, PageY = y, PageW = w };

    /// <summary>
    /// Stands in for WPF: each text has a total height; what does not fit above the page's bottom
    /// continues at the top of the next page, like the real layout.
    /// </summary>
    static Func<TextItem, TextMeasure> Measure(Session session, Dictionary<string, double> heights) => t =>
    {
        var index = session.Pages.FindIndex(p => p.Id == t.PageId);
        var (top, bottom) = PageLayout.TextArea(session, session.Pages[index]);
        var total = heights[t.Id];
        var room = bottom - t.PageY;
        if (total <= room) return new TextMeasure(total, []);
        return new TextMeasure(room, [new PagedRect(index + 1, new Rect(t.PageX, top, t.PageW, total - room))]);
    };

    static Session OnePage(params object[] things) => new()
    {
        Pages = [new PdfPage { Id = "p1" }],
        Extracts = things.OfType<ExtractItem>().ToList(),
        TextItems = things.OfType<TextItem>().ToList(),
    };

    [Fact]
    public void A_layout_without_overlaps_is_left_exactly_as_it_is()
    {
        var a = Section("a", "p1", 18, 150);
        var t = Text("t", "p1", 180);
        var b = Section("b", "p1", 300, 150);
        var beside = Section("c", "p1", 18, 150, x: 330, w: 200);
        var session = OnePage(a, t, b, beside);

        PageSettler.Settle(session, Measure(session, new() { ["t"] = 100 }));

        Assert.Equal((18, 180, 300, 18), (a.PageY, t.PageY, b.PageY, beside.PageY));
    }

    [Fact]
    public void Text_dropped_over_the_top_half_of_a_diagram_goes_before_it()
    {
        var diagram = Section("d", "p1", 18, 200);
        var text = Text("t", "p1", 30);   // centre 30 + 40 = 70, above the diagram's centre at 118
        var session = OnePage(diagram, text);

        PageSettler.Settle(session, Measure(session, new() { ["t"] = 80 }), movedId: "t");

        Assert.Equal(30, text.PageY);
        Assert.Equal(30 + 80 + Gap, diagram.PageY);
    }

    [Fact]
    public void Text_dropped_over_the_bottom_half_of_a_diagram_goes_after_it()
    {
        var diagram = Section("d", "p1", 18, 200);
        var text = Text("t", "p1", 150);   // centre 190, below the diagram's centre
        var session = OnePage(diagram, text);

        PageSettler.Settle(session, Measure(session, new() { ["t"] = 80 }), movedId: "t");

        Assert.Equal(18, diagram.PageY);
        Assert.Equal(18 + 200 + Gap, text.PageY);
    }

    [Fact]
    public void A_diagram_dragged_down_past_a_text_swaps_places_with_it()
    {
        var text = Text("t", "p1", 18);
        var diagram = Section("d", "p1", 60, 200);   // dropped overlapping, its centre below the text's
        var session = OnePage(text, diagram);

        PageSettler.Settle(session, Measure(session, new() { ["t"] = 80 }), movedId: "d");

        Assert.Equal(18, text.PageY);
        Assert.Equal(18 + 80 + Gap, diagram.PageY);
    }

    [Fact]
    public void A_tall_diagram_dragged_up_over_a_short_text_at_the_top_goes_first()
    {
        var text = Text("t", "p1", 18);                 // 18 tall, centre 27
        var diagram = Section("d", "p1", 10, 200);      // dropped with its top above that centre
        var session = OnePage(text, diagram);

        PageSettler.Settle(session, Measure(session, new() { ["t"] = 18 }), movedId: "d");

        Assert.Equal(10, diagram.PageY);
        Assert.Equal(10 + 200 + Gap, text.PageY);
    }

    [Fact]
    public void Nudging_a_box_up_into_the_bottom_of_the_one_above_puts_it_back_below()
    {
        var above = Section("a", "p1", 18, 200);
        var text = Text("t", "p1", 190);   // top 190 is below the diagram's centre at 118
        var session = OnePage(above, text);

        PageSettler.Settle(session, Measure(session, new() { ["t"] = 40 }), movedId: "t");

        Assert.Equal(18 + 200 + Gap, text.PageY);
    }

    [Fact]
    public void Pushing_cascades_down_through_everything_underneath()
    {
        var a = Section("a", "p1", 18, 200);
        var b = Section("b", "p1", 230, 200);
        var c = Section("c", "p1", 442, 100);
        var text = Text("t", "p1", 10);
        var session = OnePage(a, b, c, text);

        PageSettler.Settle(session, Measure(session, new() { ["t"] = 60 }), movedId: "t");

        Assert.Equal(10 + 60 + Gap, a.PageY);
        Assert.Equal(a.PageY + 200 + Gap, b.PageY);
        Assert.Equal(b.PageY + 200 + Gap, c.PageY);
    }

    [Fact]
    public void Things_side_by_side_do_not_push_each_other()
    {
        var left = Section("a", "p1", 18, 300, x: 18, w: 260);
        var right = Text("t", "p1", 40, x: 300, w: 270);
        var session = OnePage(left, right);

        PageSettler.Settle(session, Measure(session, new() { ["t"] = 100 }), movedId: "t");

        Assert.Equal((18, 40), (left.PageY, right.PageY));
    }

    [Fact]
    public void What_no_longer_fits_moves_to_the_top_of_a_new_page_in_order()
    {
        var a = Section("a", "p1", 18, 500);
        var b = Section("b", "p1", 530, 250);
        var c = Section("c", "p1", 792, 30);
        var text = Text("t", "p1", 18);
        var session = OnePage(a, b, c, text);

        PageSettler.Settle(session, Measure(session, new() { ["t"] = 100 }), movedId: "t");

        Assert.Equal(2, session.Pages.Count);
        Assert.Equal(("p1", 130.0), (a.PageId, a.PageY));
        var next = session.Pages[1].Id;
        Assert.Equal((next, 18.0), (b.PageId, b.PageY));
        Assert.Equal((next, 18 + 250 + Gap), (c.PageId, c.PageY));
    }

    [Fact]
    public void Things_arriving_on_a_page_go_before_what_was_already_there()
    {
        var session = new Session
        {
            Pages = [new PdfPage { Id = "p1" }, new PdfPage { Id = "p2" }],
            Extracts = [Section("a", "p1", 18, 700), Section("b", "p1", 730, 100), Section("c", "p2", 18, 100)],
            TextItems = [Text("t", "p1", 18)],
        };

        PageSettler.Settle(session, Measure(session, new() { ["t"] = 40 }), movedId: "t");

        var (b, c) = (session.Extracts[1], session.Extracts[2]);
        Assert.Equal(("p2", 18.0), (b.PageId, b.PageY));
        Assert.Equal(("p2", 18 + 100 + Gap), (c.PageId, c.PageY));
    }

    [Fact]
    public void Text_running_on_to_the_next_page_pushes_that_page_down_below_it()
    {
        var session = new Session
        {
            Pages = [new PdfPage { Id = "p1" }, new PdfPage { Id = "p2" }],
            Extracts = [Section("s", "p2", 18, 100)],
            TextItems = [Text("t", "p1", 700)],
        };

        PageSettler.Settle(session, Measure(session, new() { ["t"] = 300 }));

        // 824 - 700 = 124 fits on page 1, the other 176 sits at the top of page 2
        Assert.Equal(18 + 176 + Gap, session.Extracts[0].PageY);
    }

    [Fact]
    public void Text_pushed_past_the_bottom_starts_on_the_next_page()
    {
        var session = new Session
        {
            Pages = [new PdfPage { Id = "p1" }],
            Extracts = [Section("s", "p1", 18, 790)],
            TextItems = [Text("t", "p1", 400)],
        };

        PageSettler.Settle(session, Measure(session, new() { ["t"] = 50 }));

        var text = session.TextItems[0];
        Assert.Equal(2, session.Pages.Count);
        Assert.Equal((session.Pages[1].Id, 18.0), (text.PageId, text.PageY));
    }

    // ---------------------------------------------------------------- where a drag will land

    [Fact]
    public void A_drag_over_the_top_half_lands_where_it_is()
    {
        var diagram = new Rect(18, 18, 300, 200);
        var moving = new Rect(18, 40, 300, 60);

        Assert.Equal(40, PageSettler.LandingTop([diagram], moving));
    }

    [Fact]
    public void A_drag_over_the_bottom_half_lands_below()
    {
        var diagram = new Rect(18, 18, 300, 200);
        var moving = new Rect(18, 150, 300, 60);

        Assert.Equal(18 + 200 + Gap, PageSettler.LandingTop([diagram], moving));
    }

    [Fact]
    public void A_drag_beside_something_or_on_an_empty_page_lands_where_it_is()
    {
        Assert.Equal(150, PageSettler.LandingTop([new Rect(18, 18, 250, 300)], new Rect(300, 150, 250, 60)));
        Assert.Equal(90, PageSettler.LandingTop([], new Rect(18, 90, 300, 60)));
    }

    [Fact]
    public void A_drag_lands_below_everything_before_it_in_a_column()
    {
        var a = new Rect(18, 18, 300, 100);
        var b = new Rect(18, 130, 300, 100);
        var moving = new Rect(18, 150, 300, 40);   // top 150 is past b's centre at 180? no: before b

        Assert.Equal(18 + 100 + Gap, PageSettler.LandingTop([a, b], moving with { Y = 100 }));
        Assert.Equal(130 + 100 + Gap, PageSettler.LandingTop([a, b], moving with { Y = 200 }));
    }

    // ---------------------------------------------------------------- tidy up

    [Fact]
    public void Tidying_closes_gaps_from_the_top_of_the_page_down()
    {
        var a = Section("a", "p1", 60, 100);
        var t = Text("t", "p1", 300);
        var b = Section("b", "p1", 500, 100);
        var session = OnePage(a, t, b);

        PageSettler.Compact(session, Measure(session, new() { ["t"] = 40 }));

        Assert.Equal(18, a.PageY);
        Assert.Equal(18 + 100 + Gap, t.PageY);
        Assert.Equal(t.PageY + 40 + Gap, b.PageY);
    }

    [Fact]
    public void Tidying_keeps_things_side_by_side()
    {
        var left = Section("a", "p1", 80, 200, x: 18, w: 260);
        var right = Section("b", "p1", 120, 100, x: 300, w: 260);
        var session = OnePage(left, right);

        PageSettler.Compact(session, Measure(session, []));

        Assert.Equal((18, 18), (left.PageY, right.PageY));
    }

    [Fact]
    public void Tidying_pulls_things_back_from_the_next_page_and_removes_pages_it_empties()
    {
        var session = new Session
        {
            Pages = [new PdfPage { Id = "p1" }, new PdfPage { Id = "p2" }],
            Extracts = [Section("a", "p1", 18, 100), Section("b", "p2", 300, 100)],
        };

        PageSettler.Compact(session, Measure(session, []));

        Assert.Single(session.Pages);
        Assert.Equal(("p1", 18 + 100 + Gap), (session.Extracts[1].PageId, session.Extracts[1].PageY));
    }

    [Fact]
    public void Tidying_keeps_a_page_that_was_left_blank_on_purpose()
    {
        var session = new Session
        {
            Pages = [new PdfPage { Id = "p1" }, new PdfPage { Id = "blank" }, new PdfPage { Id = "p3" }],
            Extracts = [Section("a", "p1", 18, 100), Section("b", "p3", 300, 100)],
        };

        PageSettler.Compact(session, Measure(session, []));

        Assert.Equal(["p1", "blank", "p3"], session.Pages.Select(p => p.Id));
        Assert.Equal(("p3", 18.0), (session.Extracts[1].PageId, session.Extracts[1].PageY));
    }

    [Fact]
    public void Tidying_moves_on_to_the_next_page_when_the_rest_does_not_fit()
    {
        var session = new Session
        {
            Pages = [new PdfPage { Id = "p1" }, new PdfPage { Id = "p2" }],
            Extracts = [Section("a", "p1", 100, 600), Section("b", "p1", 712, 100), Section("c", "p2", 200, 120)],
        };

        PageSettler.Compact(session, Measure(session, []));

        var (a, b, c) = (session.Extracts[0], session.Extracts[1], session.Extracts[2]);
        Assert.Equal(("p1", 18.0), (a.PageId, a.PageY));
        Assert.Equal(("p1", 18 + 600 + Gap), (b.PageId, b.PageY));
        Assert.Equal(("p2", 18.0), (c.PageId, c.PageY));   // 730 + 12 + 120 would pass the page's edge at 842
    }

    [Fact]
    public void A_moved_item_wins_a_tie_with_what_it_was_dropped_on()
    {
        var old = Text("old", "p1", 100);
        var added = Text("new", "p1", 100);
        var session = OnePage(old, added);

        PageSettler.Settle(session, Measure(session, new() { ["old"] = 40, ["new"] = 40 }), movedId: "new");

        Assert.Equal(100, added.PageY);
        Assert.Equal(100 + 40 + Gap, old.PageY);
    }
}
