using FocusLock.Core.Document;
using FocusLock.Core.Export;
using FocusLock.Core.Models;
using FocusLock.Core.Sessions;

namespace FocusLock.Core.Tests.Document;

public sealed class DocModelTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "focuslock-doc-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    static ParagraphBlock Para(string text, string style = DocStyle.Normal) =>
        new() { Style = style, Runs = [new DocRun { Text = text }] };

    [Fact]
    public void A_text_box_with_every_block_kind_survives_saving_and_loading()
    {
        var session = new Session
        {
            Id = "s1",
            PdfHeader = true, PdfHeaderText = "Notes", PdfPageNumbers = true,
            TextItems =
            [
                new TextItem
                {
                    Id = "t1", PageId = "p1", PageX = 20, PageY = 30, PageW = 400,
                    Blocks =
                    [
                        new ParagraphBlock
                        {
                            Style = DocStyle.H1, Align = DocAlign.Center,
                            Runs = [new DocRun { Text = "Plan", Bold = true, Italic = true, Underline = true, Color = "#ff0000", Highlight = "#ffff00", Size = 20 }],
                        },
                        new ParagraphBlock { List = DocList.Check, Checked = true, Runs = [new DocRun { Text = "Read 4.1" }] },
                        new CalloutBlock { Tone = CalloutTone.Important, Paragraphs = [Para("Bring the sheet")] },
                        new DividerBlock(),
                        new PageBreakBlock(),
                        new TableBlock { Rows = [[new TableCell { Paragraphs = [Para("a")] }, new TableCell { Paragraphs = [Para("b")] }]] },
                    ],
                },
            ],
        };

        var store = new SessionStore(_dir);
        store.Save(session);
        var loaded = store.Load("s1")!;

        Assert.Equal((true, "Notes", true), (loaded.PdfHeader, loaded.PdfHeaderText, loaded.PdfPageNumbers));
        var text = Assert.Single(loaded.TextItems);
        Assert.Equal(("t1", "p1", 20.0, 30.0, 400.0), (text.Id, text.PageId, text.PageX, text.PageY, text.PageW));
        Assert.Equal(6, text.Blocks.Count);

        var h = Assert.IsType<ParagraphBlock>(text.Blocks[0]);
        Assert.Equal((DocStyle.H1, DocAlign.Center), (h.Style, h.Align));
        var run = Assert.Single(h.Runs);
        Assert.Equal(("Plan", true, true, true, "#ff0000", "#ffff00", 20.0), (run.Text, run.Bold, run.Italic, run.Underline, run.Color, run.Highlight, run.Size));
        Assert.Equal((DocList.Check, true), (Assert.IsType<ParagraphBlock>(text.Blocks[1]).List, ((ParagraphBlock)text.Blocks[1]).Checked));
        Assert.Equal(CalloutTone.Important, Assert.IsType<CalloutBlock>(text.Blocks[2]).Tone);
        Assert.IsType<DividerBlock>(text.Blocks[3]);
        Assert.IsType<PageBreakBlock>(text.Blocks[4]);
        Assert.Equal("b", Assert.IsType<TableBlock>(text.Blocks[5]).Rows[0][1].Paragraphs[0].Runs[0].Text);
        Assert.Null(loaded.LegacyDocument);
    }

    [Fact]
    public void Words_from_the_old_document_mode_become_a_text_box_on_a_new_page()
    {
        var store = new SessionStore(_dir);
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "old.json"), """
            {"id":"old","name":"Doc","pdfMode":"document","pages":[{"id":"p1"}],
             "document":{"showHeader":true,"headerText":"Mine","showPageNumbers":true,
               "blocks":[{"kind":"paragraph","runs":[{"text":"Hello"}]},{"kind":"section","extractId":"x1"}]}}
            """);

        var session = store.Load("old")!;
        PageLayout.Complete(session);

        Assert.Null(session.LegacyDocument);
        Assert.Equal(2, session.Pages.Count);
        var text = Assert.Single(session.TextItems);
        Assert.Equal(session.Pages[1].Id, text.PageId);
        var only = Assert.Single(text.Blocks);   // the section stays a section on the pages, not a copy in the text
        Assert.Equal("Hello", Assert.IsType<ParagraphBlock>(only).Runs[0].Text);
        Assert.Equal((true, "Mine", true), (session.PdfHeader, session.PdfHeaderText, session.PdfPageNumbers));
    }

    [Fact]
    public void An_old_document_with_nothing_written_leaves_no_empty_box()
    {
        var session = new Session
        {
            Pages = [new PdfPage { Id = "p1" }],
            LegacyDocument = new DocModel { Blocks = [new ParagraphBlock(), new SectionBlock { ExtractId = "x" }] },
        };

        PageLayout.Complete(session);

        Assert.Empty(session.TextItems);
        Assert.Single(session.Pages);
    }

    [Fact]
    public void Normalising_merges_neighbouring_runs_with_the_same_format_and_drops_empty_ones()
    {
        List<DocRun> runs =
        [
            new() { Text = "Hel" }, new() { Text = "lo " }, new() { Text = "" },
            new() { Text = "big", Bold = true }, new() { Text = " world", Bold = true }, new() { Text = "!" },
        ];

        var result = DocOps.NormaliseRuns(runs);

        Assert.Equal(["Hello ", "big world", "!"], result.Select(r => r.Text));
        Assert.Equal([false, true, false], result.Select(r => r.Bold));
    }

    [Fact]
    public void Text_follows_the_paper_dark_on_light_papers_and_light_on_dark()
    {
        Assert.Equal(DocOps.InkOnLight, DocOps.InkFor("#ffffff"));
        Assert.Equal(DocOps.InkOnDark, DocOps.InkFor("#121315"));
        Assert.Equal(DocOps.InkOnLight, DocOps.InkFor("not a colour"));
    }
}
