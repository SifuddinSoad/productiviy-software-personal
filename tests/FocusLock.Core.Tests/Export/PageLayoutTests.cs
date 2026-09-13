using FocusLock.Core.Board;
using FocusLock.Core.Export;
using FocusLock.Core.Models;

namespace FocusLock.Core.Tests.Export;

public class PageLayoutTests
{
    const double Margin = PageLayout.Margin;

    static ExtractItem Item(string id, double w, double h) => new() { Id = id, PlanId = "p", W = w, H = h };

    [Fact]
    public void Pages_are_A4_and_turn_on_their_side_in_landscape()
    {
        Assert.Equal((595, 842), PageLayout.SizeOf(new PdfPage()));
        Assert.Equal((842, 595), PageLayout.SizeOf(new PdfPage { Landscape = true }));
    }

    [Fact]
    public void A_box_is_the_picture_plus_a_caption_band_when_titles_are_on()
    {
        var item = Item("a", 400, 200);
        item.PageX = 30; item.PageY = 40; item.PageW = 300;

        Assert.Equal(new Rect(30, 40, 300, 150), PageLayout.BoxOf(item, titles: false));
        Assert.Equal(new Rect(30, 40, 300, 150 + PageLayout.CaptionHeight), PageLayout.BoxOf(item, titles: true));
    }

    [Fact]
    public void A_new_extract_starts_at_its_natural_size_at_the_top_of_the_first_page()
    {
        var session = new Session();
        var item = Item("a", 300, 200);   // 225 x 150 pt
        session.Extracts.Add(item);

        PageLayout.Place(session, item);

        Assert.Single(session.Pages);
        Assert.Equal(session.Pages[0].Id, item.PageId);
        Assert.Equal((Margin, Margin, 225), (item.PageX, item.PageY, item.PageW));
    }

    [Fact]
    public void The_next_extract_goes_below_the_last_one_with_a_gap()
    {
        var session = new Session();
        var a = Item("a", 300, 200);
        var b = Item("b", 300, 100);
        session.Extracts.AddRange([a, b]);

        PageLayout.Place(session, a);
        PageLayout.Place(session, b);

        Assert.Single(session.Pages);
        Assert.Equal(a.PageId, b.PageId);
        Assert.Equal(Margin, b.PageX);
        Assert.Equal(a.PageY + 150 + PageLayout.Gap, b.PageY, 3);
    }

    [Fact]
    public void An_extract_that_does_not_fit_below_starts_a_new_page()
    {
        var session = new Session();
        var tall = Item("a", 500, 1000);   // 375 x 750 pt, fills most of a portrait page
        var next = Item("b", 300, 200);
        session.Extracts.AddRange([tall, next]);

        PageLayout.Place(session, tall);
        PageLayout.Place(session, next);

        Assert.Equal(2, session.Pages.Count);
        Assert.Equal(session.Pages[1].Id, next.PageId);
        Assert.Equal((Margin, Margin), (next.PageX, next.PageY));
    }

    [Fact]
    public void A_wide_extract_that_needs_a_new_page_gets_a_landscape_one()
    {
        var session = new Session();
        var tall = Item("a", 500, 1000);
        var wide = Item("b", 2000, 600);
        session.Extracts.AddRange([tall, wide]);

        PageLayout.Place(session, tall);
        PageLayout.Place(session, wide);

        Assert.False(session.Pages[0].Landscape);
        Assert.True(session.Pages[1].Landscape);
    }

    [Fact]
    public void An_extract_larger_than_the_page_is_shrunk_to_the_printable_area()
    {
        var session = new Session();
        var huge = Item("a", 4000, 6000);
        session.Extracts.Add(huge);

        PageLayout.Place(session, huge);

        var (pw, ph) = PageLayout.SizeOf(session.Pages[0]);
        var box = PageLayout.BoxOf(huge, titles: false);
        Assert.True(box.Right <= pw - Margin + 0.01);
        Assert.True(box.Bottom <= ph - Margin + 0.01);
        Assert.Equal(huge.H / huge.W, box.H / box.W, 6);
    }

    [Fact]
    public void Completing_an_old_session_places_every_unplaced_extract_in_list_order()
    {
        var session = new Session();
        session.Extracts.AddRange([Item("a", 300, 200), Item("b", 300, 200), Item("c", 300, 200)]);

        PageLayout.Complete(session);

        Assert.All(session.Extracts, e => Assert.Contains(session.Pages, p => p.Id == e.PageId));
        Assert.True(session.Extracts[0].PageY < session.Extracts[1].PageY);
        Assert.True(session.Extracts[1].PageY < session.Extracts[2].PageY);
    }

    [Fact]
    public void Completing_gives_an_empty_session_one_page_and_repairs_a_missing_page_reference()
    {
        var empty = new Session();
        PageLayout.Complete(empty);
        Assert.Single(empty.Pages);

        var session = new Session { Pages = [new PdfPage { Id = "pg1" }] };
        var lost = Item("a", 300, 200);
        lost.PageId = "gone";
        session.Extracts.Add(lost);

        PageLayout.Complete(session);

        Assert.Equal("pg1", lost.PageId);
    }

    [Fact]
    public void Clamping_pulls_a_box_back_onto_its_page()
    {
        var page = new PdfPage { Id = "p" };
        var item = Item("a", 400, 200);
        item.PageId = "p"; item.PageX = 500; item.PageY = -40; item.PageW = 200;

        PageLayout.Clamp(item, page, titles: false);

        Assert.Equal(595 - 200, item.PageX, 3);
        Assert.Equal(0, item.PageY, 3);
        Assert.Equal(200, item.PageW, 3);
    }

    [Fact]
    public void Clamping_shrinks_a_box_too_big_for_its_page_to_fit_inside_the_margins()
    {
        var page = new PdfPage { Id = "p" };
        var item = Item("a", 1000, 1000);
        item.PageId = "p"; item.PageX = 0; item.PageY = 0; item.PageW = 800;

        PageLayout.Clamp(item, page, titles: true);

        var box = PageLayout.BoxOf(item, titles: true);
        Assert.Equal(595 - 2 * Margin, box.W, 3);
        Assert.True(box.X >= 0 && box.Right <= 595 + 0.01);
        Assert.True(box.Bottom <= 842 + 0.01);
    }

    [Fact]
    public void An_empty_region_does_not_produce_a_broken_box()
    {
        var session = new Session();
        var item = Item("a", 0, 0);
        session.Extracts.Add(item);

        PageLayout.Place(session, item);

        var box = PageLayout.BoxOf(item, titles: false);
        Assert.True(box.W > 0 && box.H > 0);
        Assert.False(double.IsNaN(box.H));
    }
}
