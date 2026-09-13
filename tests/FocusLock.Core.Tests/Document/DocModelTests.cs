using FocusLock.Core.Document;
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
    public void Every_block_kind_survives_saving_and_loading_with_the_session()
    {
        var session = new Session
        {
            Id = "s1",
            PdfMode = PdfMode.Document,
            Document = new DocModel
            {
                Landscape = true, Paper = "#fbf7ee", ShowHeader = false, HeaderText = "Notes", ShowPageNumbers = true,
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
                    new SectionBlock { ExtractId = "x1", Size = SectionSize.Medium, Align = DocAlign.Right, Caption = false },
                ],
            },
        };

        var store = new SessionStore(_dir);
        store.Save(session);
        var loaded = store.Load("s1")!;

        Assert.Equal(PdfMode.Document, loaded.PdfMode);
        var doc = loaded.Document;
        Assert.True(doc.Landscape);
        Assert.Equal(("#fbf7ee", false, "Notes", true), (doc.Paper, doc.ShowHeader, doc.HeaderText, doc.ShowPageNumbers));
        Assert.Equal(7, doc.Blocks.Count);

        var h = Assert.IsType<ParagraphBlock>(doc.Blocks[0]);
        Assert.Equal((DocStyle.H1, DocAlign.Center), (h.Style, h.Align));
        var run = Assert.Single(h.Runs);
        Assert.Equal(("Plan", true, true, true, "#ff0000", "#ffff00", 20.0), (run.Text, run.Bold, run.Italic, run.Underline, run.Color, run.Highlight, run.Size));

        var check = Assert.IsType<ParagraphBlock>(doc.Blocks[1]);
        Assert.Equal((DocList.Check, true), (check.List, check.Checked));
        Assert.Equal(CalloutTone.Important, Assert.IsType<CalloutBlock>(doc.Blocks[2]).Tone);
        Assert.IsType<DividerBlock>(doc.Blocks[3]);
        Assert.IsType<PageBreakBlock>(doc.Blocks[4]);
        Assert.Equal("b", Assert.IsType<TableBlock>(doc.Blocks[5]).Rows[0][1].Paragraphs[0].Runs[0].Text);
        var section = Assert.IsType<SectionBlock>(doc.Blocks[6]);
        Assert.Equal(("x1", SectionSize.Medium, DocAlign.Right, false), (section.ExtractId, section.Size, section.Align, section.Caption));
    }

    [Fact]
    public void An_older_session_without_a_document_loads_in_free_layout_with_an_empty_document()
    {
        var store = new SessionStore(_dir);
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "old.json"), """{"id":"old","name":"Before documents","plans":[]}""");

        var loaded = store.Load("old")!;

        Assert.Equal(PdfMode.Free, loaded.PdfMode);
        Assert.Empty(loaded.Document.Blocks);
        Assert.Equal("#ffffff", loaded.Document.Paper);
    }

    [Fact]
    public void Seeding_starts_with_an_empty_paragraph_then_every_extract_in_list_order()
    {
        var session = new Session { Extracts = [new ExtractItem { Id = "a" }, new ExtractItem { Id = "b" }] };

        DocOps.Seed(session);

        var blocks = session.Document.Blocks;
        Assert.Equal(3, blocks.Count);
        Assert.IsType<ParagraphBlock>(blocks[0]);
        var first = Assert.IsType<SectionBlock>(blocks[1]);
        Assert.Equal(("a", SectionSize.Full, DocAlign.Center, true), (first.ExtractId, first.Size, first.Align, first.Caption));
        Assert.Equal("b", Assert.IsType<SectionBlock>(blocks[2]).ExtractId);
    }

    [Fact]
    public void Seeding_leaves_a_document_that_already_has_content_alone()
    {
        var session = new Session { Extracts = [new ExtractItem { Id = "a" }] };
        session.Document.Blocks.Add(Para("mine"));

        DocOps.Seed(session);

        Assert.Single(session.Document.Blocks);
    }

    [Fact]
    public void A_new_extract_is_appended_as_a_section_once()
    {
        var doc = new DocModel { Blocks = [Para("x")] };

        DocOps.AppendSection(doc, "a");
        DocOps.AppendSection(doc, "a");

        Assert.Equal(2, doc.Blocks.Count);
        Assert.Equal("a", Assert.IsType<SectionBlock>(doc.Blocks[1]).ExtractId);
    }

    [Fact]
    public void Removing_an_extract_takes_every_picture_of_it_out_of_the_document()
    {
        var doc = new DocModel
        {
            Blocks = [new SectionBlock { ExtractId = "a" }, Para("x"), new SectionBlock { ExtractId = "b" }, new SectionBlock { ExtractId = "a" }],
        };

        DocOps.RemoveSections(doc, "a");

        Assert.Equal(2, doc.Blocks.Count);
        Assert.DoesNotContain(doc.Blocks, b => b is SectionBlock { ExtractId: "a" });
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
    public void Section_width_is_a_share_of_the_text_width()
    {
        Assert.Equal(40, DocOps.SectionWidth(SectionSize.Small, 100), 6);
        Assert.Equal(65, DocOps.SectionWidth(SectionSize.Medium, 100), 6);
        Assert.Equal(100, DocOps.SectionWidth(SectionSize.Full, 100), 6);
        Assert.Equal(100, DocOps.SectionWidth("unknown", 100), 6);
    }

    [Fact]
    public void Text_follows_the_paper_dark_on_light_papers_and_light_on_dark()
    {
        Assert.Equal(DocOps.InkOnLight, DocOps.InkFor("#ffffff"));
        Assert.Equal(DocOps.InkOnLight, DocOps.InkFor("#fbf7ee"));
        Assert.Equal(DocOps.InkOnDark, DocOps.InkFor("#121315"));
        Assert.Equal(DocOps.InkOnLight, DocOps.InkFor("not a colour"));
    }
}
