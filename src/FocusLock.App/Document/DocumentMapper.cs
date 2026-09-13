using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FocusLock.App.Theme;
using FocusLock.Core.Document;
using Fonts = FocusLock.App.Board.Fonts;
using WpfList = System.Windows.Documents.List;
using WpfSection = System.Windows.Documents.Section;
using WpfTable = System.Windows.Documents.Table;
using WpfTableCell = System.Windows.Documents.TableCell;

namespace FocusLock.App.Document;

/// <summary>
/// Turns a text box's blocks into a WPF <see cref="FlowDocument"/> and reads one back.
///
/// The same blocks build two documents: the one being edited, where a page break is a visible
/// marker and checklist boxes can be clicked, and one for laying out on pages, where a page break
/// really moves the rest of the text to the next page.
/// Structure is marked with <c>Tag</c>: a paragraph's style, a checklist, a callout's tone, so
/// reading back does not have to guess from formatting.
/// </summary>
public static class DocumentMapper
{
    const string CheckTag = "check";
    const string CalloutPrefix = "callout:";
    const string DividerTag = "divider";
    const string PageBreakTag = "pageBreak";
    const string TableTag = "table";

    // ================================================================ model -> FlowDocument

    /// <param name="widthDip">The text box's width; text wraps inside it with no padding of its own.</param>
    public static FlowDocument Build(IReadOnlyList<DocBlock> blocks, string paper, double widthDip, bool forPrint)
    {
        var doc = new FlowDocument
        {
            FontFamily = DocLook.TextFont,
            FontSize = DocLook.Dip(DocLook.StyleSizePt(DocStyle.Normal)),
            Foreground = DocLook.Ink(paper),
            PageWidth = widthDip,
            PagePadding = new Thickness(0),
            ColumnWidth = widthDip,   // one column, however wide the box
            IsHyphenationEnabled = false,
        };

        var breakNext = false;
        foreach (var group in GroupLists(blocks))
        {
            if (group is PageBreakBlock && forPrint) { breakNext = true; continue; }

            var block = BuildBlock(group, paper);
            if (block is null) continue;
            if (breakNext) { block.BreakPageBefore = true; breakNext = false; }
            doc.Blocks.Add(block);
        }

        if (doc.Blocks.Count == 0) doc.Blocks.Add(NewParagraph(new ParagraphBlock()));
        return doc;
    }

    /// <summary>Neighbouring list paragraphs of one kind become one list; everything else passes through.</summary>
    static IEnumerable<object> GroupLists(IEnumerable<DocBlock> blocks)
    {
        List<ParagraphBlock>? run = null;
        foreach (var block in blocks)
        {
            if (block is ParagraphBlock { List: not DocList.None } p)
            {
                if (run is not null && run[0].List != p.List) { yield return run; run = null; }
                (run ??= []).Add(p);
                continue;
            }
            if (run is not null) { yield return run; run = null; }
            yield return block;
        }
        if (run is not null) yield return run;
    }

    static Block? BuildBlock(object item, string paper) => item switch
    {
        List<ParagraphBlock> list => NewList(list),
        ParagraphBlock p => NewParagraph(p),
        CalloutBlock c => NewCallout(c),
        DividerBlock => NewDivider(paper),
        PageBreakBlock => NewPageBreakMarker(paper),
        TableBlock t => NewTable(t, paper),
        _ => null,   // pictures live on the pages as sections, not inside text
    };

    public static Paragraph NewParagraph(ParagraphBlock p)
    {
        var para = new Paragraph();
        ApplyStyle(para, p.Style);
        para.TextAlignment = p.Align switch
        {
            DocAlign.Center => TextAlignment.Center,
            DocAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Left,
        };
        foreach (var run in p.Runs) AddRun(para.Inlines, run);
        return para;
    }

    /// <summary>A paragraph's style is its tag plus the size, weight and spacing that go with it.</summary>
    public static void ApplyStyle(Paragraph para, string style)
    {
        para.Tag = style == DocStyle.Normal ? null : style;
        para.FontSize = DocLook.Dip(DocLook.StyleSizePt(style));
        para.FontWeight = DocLook.StyleWeight(style);
        para.Margin = para.Parent is ListItem ? DocLook.ListItemMargin : DocLook.StyleMargin(style);
    }

    public static string StyleOf(Paragraph para) =>
        para.Tag is string s && s is DocStyle.Title or DocStyle.H1 or DocStyle.H2 ? s : DocStyle.Normal;

    static void AddRun(InlineCollection inlines, DocRun r)
    {
        var parts = r.Text.Split('\n');
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0) inlines.Add(new LineBreak());
            if (parts[i].Length == 0) continue;

            var run = new Run(parts[i]);
            if (r.Bold) run.FontWeight = FontWeights.Bold;
            if (r.Italic) run.FontStyle = FontStyles.Italic;
            if (r.Underline) run.TextDecorations = TextDecorations.Underline;
            if (r.Color is { } c) run.Foreground = HexBrush.FromHex(c);
            if (r.Highlight is { } h) run.Background = HexBrush.FromHex(h);
            if (r.Size is { } size) run.FontSize = DocLook.Dip(size);
            inlines.Add(run);
        }
    }

    static WpfList NewList(List<ParagraphBlock> items)
    {
        var kind = items[0].List;
        var list = new WpfList
        {
            MarkerStyle = kind switch
            {
                DocList.Number => TextMarkerStyle.Decimal,
                DocList.Check => TextMarkerStyle.None,
                _ => TextMarkerStyle.Disc,
            },
            Tag = kind == DocList.Check ? CheckTag : null,
            Margin = new Thickness(0, 0, 0, DocLook.Dip(6)),
            Padding = new Thickness(kind == DocList.Check ? DocLook.Dip(4) : DocLook.Dip(20), 0, 0, 0),
        };
        foreach (var p in items)
        {
            var para = NewParagraph(p);
            var listItem = new ListItem(para);
            para.Margin = DocLook.ListItemMargin;
            if (kind == DocList.Check) AddCheckBox(para, p.Checked);
            list.ListItems.Add(listItem);
        }
        return list;
    }

    /// <summary>The box at the start of a checklist item. It is an element, not text, so typing after it never picks up the icon font.</summary>
    public static InlineUIContainer NewCheckBox(bool ticked)
    {
        var glyph = new TextBlock
        {
            Text = ticked ? DocLook.BoxTicked : DocLook.BoxEmpty,
            FontFamily = Fonts.Icons,
            FontSize = DocLook.Dip(13),
            Tag = ticked,
            Margin = new Thickness(0, 0, DocLook.Dip(5), 0),
            Cursor = Cursors.Hand,
        };
        return new InlineUIContainer(glyph) { Tag = CheckTag, BaselineAlignment = BaselineAlignment.Center };
    }

    public static bool IsCheckBox(Inline inline) => inline is InlineUIContainer { Tag: CheckTag };

    public static void AddCheckBox(Paragraph para, bool ticked)
    {
        if (para.Inlines.FirstInline is { } first && IsCheckBox(first)) return;
        if (para.Inlines.FirstInline is null) para.Inlines.Add(NewCheckBox(ticked));
        else para.Inlines.InsertBefore(para.Inlines.FirstInline, NewCheckBox(ticked));
    }

    public static void Toggle(InlineUIContainer box)
    {
        if (box.Child is not TextBlock glyph) return;
        var ticked = glyph.Tag is not true;
        glyph.Tag = ticked;
        glyph.Text = ticked ? DocLook.BoxTicked : DocLook.BoxEmpty;
    }

    public static bool IsChecklist(WpfList list) => list.Tag is CheckTag;

    static WpfSection NewCallout(CalloutBlock c)
    {
        var (fill, edge) = DocLook.Callout(c.Tone);
        var section = new WpfSection
        {
            Tag = CalloutPrefix + c.Tone,
            Background = fill,
            BorderBrush = edge,
            BorderThickness = new Thickness(DocLook.Dip(3), 0, 0, 0),
            Padding = new Thickness(DocLook.Dip(10), DocLook.Dip(6), DocLook.Dip(10), DocLook.Dip(2)),
            Margin = new Thickness(0, DocLook.Dip(4), 0, DocLook.Dip(10)),
            Foreground = HexBrush.FromHex(DocOps.InkOnLight),   // callouts are always light
        };
        var paragraphs = c.Paragraphs.Count > 0 ? c.Paragraphs : [new ParagraphBlock()];
        foreach (var group in GroupLists(paragraphs))
            section.Blocks.Add(group is List<ParagraphBlock> l ? NewList(l) : NewParagraph((ParagraphBlock)group));
        return section;
    }

    public static string? CalloutTone(WpfSection section) =>
        section.Tag is string s && s.StartsWith(CalloutPrefix, StringComparison.Ordinal) ? s[CalloutPrefix.Length..] : null;

    public static WpfSection NewCalloutFor(string tone) => NewCallout(new CalloutBlock { Tone = tone });

    public static BlockUIContainer NewDivider(string paper) => new(new Border
    {
        Height = 1,
        Background = DocLook.Rule(paper),
        Margin = new Thickness(0, DocLook.Dip(6), 0, DocLook.Dip(6)),
    })
    { Tag = DividerTag };

    /// <summary>Only in the editor; when printing, a page break is the next block starting a new page.</summary>
    public static BlockUIContainer NewPageBreakMarker(string paper)
    {
        var grid = new Grid { Margin = new Thickness(0, DocLook.Dip(4), 0, DocLook.Dip(4)) };
        grid.Children.Add(new Rectangle
        {
            Height = 1, VerticalAlignment = VerticalAlignment.Center,
            Stroke = DocLook.Muted(paper), StrokeDashArray = [4, 3], StrokeThickness = 1,
        });
        grid.Children.Add(new Border
        {
            Background = HexBrush.FromHex(paper), Padding = new Thickness(8, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock { Text = "Page break", FontSize = 11, Foreground = DocLook.Muted(paper), FontFamily = Fonts.Sans },
        });
        return new BlockUIContainer(grid) { Tag = PageBreakTag };
    }

    public static WpfTable NewTable(TableBlock t, string paper)
    {
        var columns = Math.Max(1, t.Rows.Count == 0 ? 1 : t.Rows.Max(r => r.Count));
        var table = new WpfTable { Tag = TableTag, CellSpacing = 0, Margin = new Thickness(0, DocLook.Dip(4), 0, DocLook.Dip(10)) };
        for (var i = 0; i < columns; i++) table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        var rows = t.Rows.Count > 0 ? t.Rows : [[new Core.Document.TableCell()]];
        for (var r = 0; r < rows.Count; r++)
        {
            var row = new TableRow();
            for (var c = 0; c < columns; c++)
            {
                var source = c < rows[r].Count ? rows[r][c] : new Core.Document.TableCell();
                row.Cells.Add(NewCell(source.Paragraphs, header: r == 0, paper));
            }
            group.Rows.Add(row);
        }
        return table;
    }

    public static WpfTableCell NewCell(IReadOnlyList<ParagraphBlock> paragraphs, bool header, string paper)
    {
        var cell = new WpfTableCell
        {
            BorderBrush = DocLook.Rule(paper),
            BorderThickness = new Thickness(0.75),
            Padding = new Thickness(DocLook.Dip(6), DocLook.Dip(3), DocLook.Dip(6), DocLook.Dip(1)),
        };
        if (header) { cell.Background = DocLook.TableHeader(paper); cell.FontWeight = FontWeights.SemiBold; }
        var list = paragraphs.Count > 0 ? paragraphs : [new ParagraphBlock()];
        foreach (var group in GroupLists(list))
            cell.Blocks.Add(group is List<ParagraphBlock> l ? NewList(l) : NewParagraph((ParagraphBlock)group));
        foreach (var p in cell.Blocks.OfType<Paragraph>()) p.Margin = new Thickness(0, 0, 0, DocLook.Dip(2));
        return cell;
    }

    public static bool IsTable(WpfTable table) => table.Tag is TableTag;

    // ================================================================ FlowDocument -> model

    public static List<DocBlock> Read(FlowDocument doc)
    {
        var blocks = new List<DocBlock>();
        foreach (var block in doc.Blocks) ReadTopLevel(block, blocks);
        return blocks;
    }

    static void ReadTopLevel(Block block, List<DocBlock> into)
    {
        switch (block)
        {
            case WpfSection section when CalloutTone(section) is { } tone:
                into.Add(new CalloutBlock { Tone = tone, Paragraphs = ReadParagraphs(section.Blocks) });
                break;
            case WpfTable table:
                into.Add(ReadTable(table));
                break;
            case BlockUIContainer { Tag: DividerTag }:
                into.Add(new DividerBlock());
                break;
            case BlockUIContainer { Tag: PageBreakTag }:
                into.Add(new PageBreakBlock());
                break;
            case BlockUIContainer:
                break;   // nothing else is ever put in a document
            case WpfSection plain:
                foreach (var inner in plain.Blocks) ReadTopLevel(inner, into);
                break;
            default:
                into.AddRange(ReadParagraphs([block]));
                break;
        }
    }

    /// <summary>Paragraphs and list items, flattened; anything fancier inside a callout or cell is read as its text.</summary>
    static List<ParagraphBlock> ReadParagraphs(IEnumerable<Block> blocks)
    {
        var result = new List<ParagraphBlock>();
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph para:
                    result.Add(ReadParagraph(para, DocList.None));
                    break;
                case WpfList list:
                    ReadList(list, result);
                    break;
                case WpfSection section:
                    result.AddRange(ReadParagraphs(section.Blocks));
                    break;
                case WpfTable table:
                    foreach (var cell in table.RowGroups.SelectMany(g => g.Rows).SelectMany(r => r.Cells))
                        result.AddRange(ReadParagraphs(cell.Blocks));
                    break;
            }
        }
        return result;
    }

    static void ReadList(WpfList list, List<ParagraphBlock> into)
    {
        var kind = IsChecklist(list) ? DocList.Check
            : list.MarkerStyle is TextMarkerStyle.Decimal or TextMarkerStyle.LowerLatin or TextMarkerStyle.UpperLatin
                or TextMarkerStyle.LowerRoman or TextMarkerStyle.UpperRoman ? DocList.Number
            : DocList.Bullet;

        foreach (var item in list.ListItems)
            foreach (var block in item.Blocks)
            {
                if (block is Paragraph para) into.Add(ReadParagraph(para, kind));
                else if (block is WpfList nested) ReadList(nested, into);
                else into.AddRange(ReadParagraphs([block]));
            }
    }

    public static ParagraphBlock ReadParagraph(Paragraph para, string list)
    {
        var p = new ParagraphBlock
        {
            Style = StyleOf(para),
            List = list,
            Align = para.TextAlignment switch
            {
                TextAlignment.Center => DocAlign.Center,
                TextAlignment.Right => DocAlign.Right,
                _ => DocAlign.Left,
            },
        };

        var runs = new List<DocRun>();
        ReadInlines(para.Inlines, runs, p);
        p.Runs = DocOps.NormaliseRuns(runs);
        return p;
    }

    static void ReadInlines(InlineCollection inlines, List<DocRun> runs, ParagraphBlock p)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    runs.Add(ReadRun(run));
                    break;
                case LineBreak br:
                    runs.Add(FormatOf(br, "\n"));
                    break;
                case InlineUIContainer box when IsCheckBox(box):
                    p.Checked = box.Child is TextBlock { Tag: true };
                    break;
                case Span span:
                    ReadInlines(span.Inlines, runs, p);
                    break;
            }
        }
    }

    static DocRun ReadRun(Run run) => FormatOf(run, run.Text);

    /// <summary>
    /// A run's own formatting: the first local value found walking up through its spans, stopping
    /// before the paragraph, whose size and weight belong to its style rather than to the text.
    /// </summary>
    static DocRun FormatOf(Inline inline, string text)
    {
        object? Local(DependencyProperty dp)
        {
            for (DependencyObject? e = inline; e is Inline; e = (e as TextElement)?.Parent as DependencyObject)
            {
                var value = e.ReadLocalValue(dp);
                if (value != DependencyProperty.UnsetValue) return value;
            }
            return null;
        }

        var run = new DocRun { Text = text };
        if (Local(TextElement.FontWeightProperty) is FontWeight w) run.Bold = w.ToOpenTypeWeight() >= 600;
        if (Local(TextElement.FontStyleProperty) is FontStyle s) run.Italic = s == FontStyles.Italic || s == FontStyles.Oblique;
        if (Local(Inline.TextDecorationsProperty) is TextDecorationCollection d)
            run.Underline = d.Any(x => x.Location == TextDecorationLocation.Underline);
        if (Local(TextElement.ForegroundProperty) is SolidColorBrush fg) run.Color = Hex(fg.Color);
        if (Local(TextElement.BackgroundProperty) is SolidColorBrush bg && bg.Color.A > 0) run.Highlight = Hex(bg.Color);
        if (Local(TextElement.FontSizeProperty) is double size) run.Size = Math.Round(size / DocLook.DipPerPoint * 2) / 2;
        return run;
    }

    static string Hex(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

    static TableBlock ReadTable(WpfTable table)
    {
        var t = new TableBlock();
        foreach (var row in table.RowGroups.SelectMany(g => g.Rows))
            t.Rows.Add(row.Cells.Select(c => new Core.Document.TableCell { Paragraphs = ReadParagraphs(c.Blocks) }).ToList());
        return t;
    }
}
