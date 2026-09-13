using FocusLock.Core.Export;
using FocusLock.Core.Models;

namespace FocusLock.Core.Tests.Export;

public class PageDeckTests
{
    static (Session Session, PageDeck Deck) TwoPages()
    {
        var session = new Session
        {
            Pages = [new PdfPage { Id = "p1" }, new PdfPage { Id = "p2" }],
            Extracts =
            [
                new ExtractItem { Id = "a", PlanId = "pl", W = 400, H = 200, PageId = "p1", PageX = 18, PageY = 18, PageW = 300 },
                new ExtractItem { Id = "b", PlanId = "pl", W = 400, H = 200, PageId = "p2", PageX = 18, PageY = 18, PageW = 300 },
            ],
        };
        return (session, new PageDeck(session));
    }

    [Fact]
    public void Placing_moves_a_section_onto_another_page_and_keeps_it_on_the_sheet()
    {
        var (session, deck) = TwoPages();

        deck.Place("a", "p2", 500, 800, 300);

        var a = session.Extracts[0];
        Assert.Equal("p2", a.PageId);
        Assert.Equal(595 - 300, a.PageX, 3);
        Assert.Equal(842 - 150, a.PageY, 3);
    }

    [Fact]
    public void Adding_a_page_appends_a_portrait_one()
    {
        var (session, deck) = TwoPages();

        var page = deck.AddPage();

        Assert.Same(page, session.Pages[^1]);
        Assert.False(page.Landscape);
        Assert.NotEqual("", page.Id);
    }

    [Fact]
    public void Deleting_a_page_hands_its_sections_to_the_previous_page()
    {
        var (session, deck) = TwoPages();

        Assert.True(deck.DeletePage("p2"));

        Assert.Single(session.Pages);
        Assert.All(session.Extracts, e => Assert.Equal("p1", e.PageId));
    }

    [Fact]
    public void Deleting_the_first_page_hands_its_sections_to_the_next_one()
    {
        var (session, deck) = TwoPages();

        deck.DeletePage("p1");

        Assert.Equal("p2", session.Extracts[0].PageId);
    }

    [Fact]
    public void The_last_page_cannot_be_deleted()
    {
        var (session, deck) = TwoPages();
        deck.DeletePage("p1");

        Assert.False(deck.DeletePage("p2"));
        Assert.Single(session.Pages);
    }

    [Fact]
    public void Turning_a_page_clamps_what_no_longer_fits()
    {
        var (session, deck) = TwoPages();
        var a = session.Extracts[0];
        a.PageX = 500; a.PageW = 320;   // fits a landscape page, not a portrait one
        session.Pages[0].Landscape = true;

        deck.SetLandscape("p1", false);

        Assert.True(a.PageX + a.PageW <= 595 + 0.01);
    }

    [Fact]
    public void Turning_titles_on_clamps_boxes_that_would_run_off_the_bottom()
    {
        var (session, deck) = TwoPages();
        var a = session.Extracts[0];
        a.PageY = 842 - 150;   // exactly at the bottom without a caption

        deck.SetTitles(true);

        Assert.True(session.PdfTitles);
        Assert.True(PageLayout.BoxOf(a, titles: true).Bottom <= 842 + 0.01);
    }

    [Fact]
    public void Removing_a_section_takes_it_out_of_the_session()
    {
        var (session, deck) = TwoPages();

        deck.Remove("a");

        Assert.DoesNotContain(session.Extracts, e => e.Id == "a");
    }

    [Fact]
    public void Undo_and_redo_restore_pages_positions_and_removed_sections()
    {
        var (session, deck) = TwoPages();

        deck.Snapshot();
        deck.Place("a", "p1", 100, 200, 250);
        deck.DeletePage("p2");
        deck.Remove("a");

        deck.Undo();   // remove
        deck.Undo();   // delete page
        deck.Undo();   // move

        Assert.Equal(2, session.Pages.Count);
        Assert.Equal((18, 18, 300), (session.Extracts[0].PageX, session.Extracts[0].PageY, session.Extracts[0].PageW));
        Assert.Equal("p2", session.Extracts[1].PageId);

        deck.Redo();
        Assert.Equal((100, 200, 250), (session.Extracts[0].PageX, session.Extracts[0].PageY, session.Extracts[0].PageW));
        Assert.True(deck.CanRedo);
    }

    [Fact]
    public void Undo_keeps_the_same_item_objects_so_views_holding_them_stay_valid()
    {
        var (session, deck) = TwoPages();
        var a = session.Extracts[0];

        deck.Snapshot();
        deck.Place("a", "p1", 100, 200, 250);
        deck.Undo();

        Assert.Same(a, session.Extracts[0]);
        Assert.Equal(18, a.PageX);
    }
}
