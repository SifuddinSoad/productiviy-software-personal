using FocusLock.Core.Document;
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
    public void A_text_box_is_added_on_its_page_and_kept_there()
    {
        var (session, deck) = TwoPages();

        var text = deck.AddText("p1", 590, 900, 300, [new ParagraphBlock()]);

        Assert.Same(text, Assert.Single(session.TextItems));
        Assert.Equal(595 - 300, text.PageX, 3);
        Assert.Equal(842 - PageLayout.Margin - PageLayout.MinTextRoom, text.PageY, 3);
        Assert.True(deck.CanUndo);
    }

    [Fact]
    public void A_text_box_can_move_to_another_page_and_is_never_narrower_than_the_minimum()
    {
        var (session, deck) = TwoPages();
        var text = deck.AddText("p1", 18, 18, 300, []);

        deck.PlaceText(text.Id, "p2", 40, 50, 10);

        Assert.Equal(("p2", 40.0, 50.0, PageLayout.MinTextWidth), (text.PageId, text.PageX, text.PageY, text.PageW));
    }

    [Fact]
    public void Deleting_a_page_hands_its_text_boxes_to_the_previous_page()
    {
        var (session, deck) = TwoPages();
        var text = deck.AddText("p2", 18, 18, 300, []);

        deck.DeletePage("p2");

        Assert.Equal("p1", text.PageId);
    }

    [Fact]
    public void Undo_brings_back_a_text_box_and_its_earlier_words()
    {
        var (session, deck) = TwoPages();
        var text = deck.AddText("p1", 18, 18, 300, [new ParagraphBlock { Runs = [new DocRun { Text = "before" }] }]);

        deck.Snapshot();
        deck.SetText(text.Id, [new ParagraphBlock { Runs = [new DocRun { Text = "after" }] }]);
        deck.RemoveText(text.Id);

        deck.Undo();
        deck.Undo();

        var back = Assert.Single(session.TextItems);
        Assert.Equal("before", ((ParagraphBlock)back.Blocks[0]).Runs[0].Text);
    }

    [Fact]
    public void Pages_are_added_after_a_page_for_running_text_turned_the_same_way()
    {
        var (session, deck) = TwoPages();
        session.Pages[1].Landscape = true;

        deck.EnsurePagesAfter("p2", 2);
        deck.EnsurePagesAfter("p1", 1);

        Assert.Equal(4, session.Pages.Count);
        Assert.True(session.Pages[2].Landscape && session.Pages[3].Landscape);
    }

    [Fact]
    public void Header_and_page_numbers_are_undoable_settings()
    {
        var (session, deck) = TwoPages();

        deck.SetHeader(true);
        deck.SetHeaderText("  Revision  ");
        deck.SetPageNumbers(true);
        Assert.Equal((true, "Revision", true), (session.PdfHeader, session.PdfHeaderText, session.PdfPageNumbers));

        deck.Undo(); deck.Undo(); deck.Undo();
        Assert.Equal((false, "", false), (session.PdfHeader, session.PdfHeaderText, session.PdfPageNumbers));
    }

    [Fact]
    public void Turning_the_header_on_moves_boxes_out_of_its_band()
    {
        var (session, deck) = TwoPages();
        var text = deck.AddText("p1", 18, 18, 300, []);
        var low = deck.AddText("p1", 18, 300, 300, []);

        deck.SetHeader(true);

        Assert.Equal(PageLayout.HeaderBand, text.PageY);
        Assert.Equal(PageLayout.HeaderBand, session.Extracts[0].PageY);
        Assert.Equal(300, low.PageY);
    }

    [Fact]
    public void A_snapshot_that_came_to_nothing_can_be_dropped()
    {
        var (session, deck) = TwoPages();
        deck.AddPage();

        deck.Snapshot();
        deck.DiscardSnapshot();
        deck.Undo();

        Assert.Equal(2, session.Pages.Count);   // the undo went to the added page, not to the empty step
    }

    [Fact]
    public void Text_runs_between_the_header_and_page_number_bands_when_they_are_on()
    {
        var session = new Session();
        var page = new PdfPage();
        Assert.Equal((PageLayout.Margin, 842 - PageLayout.Margin), PageLayout.TextArea(session, page));

        session.PdfHeader = true;
        session.PdfPageNumbers = true;
        Assert.Equal((PageLayout.HeaderBand, 842 - PageLayout.FooterBand), PageLayout.TextArea(session, page));
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
