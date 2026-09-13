using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using FocusLock.App.Theme;
using FocusLock.Core.Document;
using WpfList = System.Windows.Documents.List;
using WpfSection = System.Windows.Documents.Section;
using WpfTable = System.Windows.Documents.Table;
using WpfTableCell = System.Windows.Documents.TableCell;

namespace FocusLock.App.Document;

/// <summary>
/// Everything a text box's editor does to its <see cref="RichTextBox"/>: styles, lists, blocks,
/// tables, and the house rules (plain-text paste, Enter after a heading, leaving a list or callout,
/// checklist boxes). Structural changes are wrapped in one change so a single Ctrl+Z undoes each.
/// </summary>
public sealed class DocumentEditor
{
    readonly RichTextBox _box;
    bool _tidying;

    /// <summary>The page colour the text sits on; text without a colour of its own follows it.</summary>
    public string Paper { get; private set; } = "#ffffff";

    /// <summary>The text or structure changed.</summary>
    public event Action? ContentChanged;

    /// <summary>The caret moved; the formatting bar should follow.</summary>
    public event Action? ContextChanged;

    public DocumentEditor(RichTextBox box)
    {
        _box = box;
        _box.IsDocumentEnabled = true;   // so checklist boxes can be clicked
        _box.AcceptsTab = true;
        _box.AllowDrop = false;
        _box.UndoLimit = 200;
        _box.SpellCheck.IsEnabled = false;
        _box.BorderThickness = new Thickness(0);
        _box.Padding = new Thickness(0);

        _box.TextChanged += (_, _) => OnTextChanged();
        _box.SelectionChanged += (_, _) => ContextChanged?.Invoke();
        _box.PreviewKeyDown += OnPreviewKeyDown;
        _box.PreviewMouseLeftButtonDown += OnPreviewMouseDown;
        DataObject.AddPastingHandler(_box, OnPasting);
    }

    // ---------------------------------------------------------------- load and read

    /// <summary>Puts a text box's blocks in the editor, wrapping at its width. Undo history starts again.</summary>
    public void Load(IReadOnlyList<DocBlock> blocks, string paper, double widthDip)
    {
        Paper = paper;
        var doc = DocumentMapper.Build(blocks, paper, widthDip, forPrint: false);
        _box.Document = doc;
        _box.Foreground = DocLook.Ink(paper);
        _box.CaretBrush = DocLook.Ink(paper);
        _box.Width = widthDip;
        _box.CaretPosition = doc.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        ContextChanged?.Invoke();
    }

    /// <summary>The blocks as they stand.</summary>
    public List<DocBlock> Read()
    {
        var blocks = DocumentMapper.Read(_box.Document);
        // text coloured exactly like the paper's own ink is the same as no colour at all
        var ink = DocOps.InkFor(Paper);
        foreach (var run in AllRuns(blocks)) if (string.Equals(run.Color, ink, StringComparison.OrdinalIgnoreCase)) run.Color = null;
        return blocks;
    }

    static IEnumerable<DocRun> AllRuns(IEnumerable<DocBlock> blocks)
    {
        foreach (var block in blocks)
        {
            IEnumerable<ParagraphBlock> paragraphs = block switch
            {
                ParagraphBlock p => [p],
                CalloutBlock c => c.Paragraphs,
                TableBlock t => t.Rows.SelectMany(r => r).SelectMany(c => c.Paragraphs),
                _ => [],
            };
            foreach (var run in paragraphs.SelectMany(p => p.Runs)) yield return run;
        }
    }

    /// <summary>Puts the caret in the first table cell, for a box that starts as a table.</summary>
    public void CaretToFirstCell()
    {
        if (_box.Document.Blocks.OfType<WpfTable>().FirstOrDefault()?.RowGroups[0].Rows[0].Cells[0].Blocks.FirstBlock is Paragraph p)
            _box.CaretPosition = p.ContentStart;
    }

    /// <summary>Puts the caret at the start of the first callout, for a box that starts as one.</summary>
    public void CaretToStart() =>
        _box.CaretPosition = _box.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward);

    // ---------------------------------------------------------------- where the caret is

    public Paragraph? CurrentParagraph => _box.CaretPosition.Paragraph ?? _box.Selection.Start.Paragraph;

    public string CurrentStyle => CurrentParagraph is { } p ? DocumentMapper.StyleOf(p) : DocStyle.Normal;

    public string CurrentList
    {
        get
        {
            if (CurrentParagraph?.Parent is not ListItem { Parent: WpfList list }) return DocList.None;
            if (DocumentMapper.IsChecklist(list)) return DocList.Check;
            return list.MarkerStyle == TextMarkerStyle.Decimal ? DocList.Number : DocList.Bullet;
        }
    }

    public TextAlignment CurrentAlignment => CurrentParagraph?.TextAlignment ?? TextAlignment.Left;

    public bool IsBold => _box.Selection.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight w && w.ToOpenTypeWeight() >= 700;
    public bool IsItalic => Equals(_box.Selection.GetPropertyValue(TextElement.FontStyleProperty), FontStyles.Italic);
    public bool IsUnderline => _box.Selection.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection { Count: > 0 };

    /// <summary>The size under the caret in points, or null when the selection mixes sizes.</summary>
    public double? CurrentSizePt => _box.Selection.GetPropertyValue(TextElement.FontSizeProperty) is double dip
        ? Math.Round(dip / DocLook.DipPerPoint * 2) / 2
        : null;

    public WpfTableCell? CurrentCell
    {
        get
        {
            for (DependencyObject? e = _box.CaretPosition.Parent; e is not null; e = (e as TextElement)?.Parent)
                if (e is WpfTableCell cell) return cell;
            return null;
        }
    }

    // ---------------------------------------------------------------- text formatting

    IEnumerable<Paragraph> SelectedParagraphs()
    {
        var sel = _box.Selection;
        return AllParagraphs(_box.Document.Blocks).Where(p =>
            p.ContentEnd.CompareTo(sel.Start) >= 0 && p.ContentStart.CompareTo(sel.End) <= 0);
    }

    static IEnumerable<Paragraph> AllParagraphs(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph p: yield return p; break;
                case WpfList l: foreach (var p in AllParagraphs(l.ListItems.SelectMany(i => i.Blocks))) yield return p; break;
                case WpfSection s: foreach (var p in AllParagraphs(s.Blocks)) yield return p; break;
                case WpfTable t:
                    foreach (var p in AllParagraphs(t.RowGroups.SelectMany(g => g.Rows).SelectMany(r => r.Cells).SelectMany(c => c.Blocks))) yield return p;
                    break;
            }
        }
    }

    void Change(Action action)
    {
        _box.BeginChange();
        try { action(); }
        finally { _box.EndChange(); }
        Tidy();
        AfterCommand();
    }

    void AfterCommand()
    {
        ContentChanged?.Invoke();
        ContextChanged?.Invoke();
        _box.Focus();
    }

    public void SetStyle(string style) => Change(() =>
    {
        foreach (var p in SelectedParagraphs().ToList()) DocumentMapper.ApplyStyle(p, style);
    });

    public void ToggleBold() { EditingCommands.ToggleBold.Execute(null, _box); AfterCommand(); }
    public void ToggleItalic() { EditingCommands.ToggleItalic.Execute(null, _box); AfterCommand(); }
    public void ToggleUnderline() { EditingCommands.ToggleUnderline.Execute(null, _box); AfterCommand(); }

    public void Align(string align)
    {
        var command = align switch
        {
            DocAlign.Center => EditingCommands.AlignCenter,
            DocAlign.Right => EditingCommands.AlignRight,
            _ => EditingCommands.AlignLeft,
        };
        command.Execute(null, _box);
        AfterCommand();
    }

    /// <summary>Steps the selection's size one notch up or down the usual ladder of point sizes.</summary>
    public void StepSize(int direction)
    {
        double[] ladder = [8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 40, 48];
        var current = CurrentSizePt ?? DocLook.StyleSizePt(CurrentStyle);
        var next = direction > 0 ? ladder.FirstOrDefault(s => s > current, ladder[^1]) : ladder.LastOrDefault(s => s < current, ladder[0]);
        _box.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, DocLook.Dip(next));
        AfterCommand();
    }

    public void SetColor(string? hex)
    {
        _box.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, hex is null ? DocLook.Ink(Paper) : HexBrush.FromHex(hex));
        AfterCommand();
    }

    public void SetHighlight(string? hex)
    {
        _box.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, hex is null ? Brushes.Transparent : HexBrush.FromHex(hex));
        AfterCommand();
    }

    // ---------------------------------------------------------------- lists

    /// <summary>Turns the selected paragraphs into a list of this kind, or back into paragraphs if they already are one.</summary>
    public void ToggleList(string kind)
    {
        var current = CurrentList;
        var command = kind == DocList.Number ? EditingCommands.ToggleNumbering : EditingCommands.ToggleBullets;
        if (current == kind)
        {
            Change(() =>
            {
                if (kind == DocList.Check) RetagListsInSelection(TextMarkerStyle.Disc, null);
                command.Execute(null, _box);
            });
            return;
        }

        Change(() =>
        {
            if (current == DocList.None) command.Execute(null, _box);
            var marker = kind switch { DocList.Number => TextMarkerStyle.Decimal, DocList.Check => TextMarkerStyle.None, _ => TextMarkerStyle.Disc };
            RetagListsInSelection(marker, kind == DocList.Check ? "check" : null);
        });
    }

    void RetagListsInSelection(TextMarkerStyle marker, string? tag)
    {
        foreach (var list in SelectedParagraphs().Select(p => (p.Parent as ListItem)?.Parent as WpfList).OfType<WpfList>().Distinct())
        {
            list.MarkerStyle = marker;
            list.Tag = tag;
            list.Padding = new Thickness(tag is null ? DocLook.Dip(20) : DocLook.Dip(4), 0, 0, 0);
        }
    }

    // ---------------------------------------------------------------- blocks

    /// <summary>The block directly in the text that holds the caret: a paragraph, list, callout or table.</summary>
    Block? TopLevelBlock()
    {
        TextElement? e = CurrentParagraph;
        while (e is not null && e.Parent is not FlowDocument) e = e.Parent as TextElement;
        return e as Block;
    }

    /// <summary>
    /// Puts a block after the one holding the caret, or in place of an empty top-level paragraph,
    /// and makes sure there is a paragraph after it to keep typing into.
    /// </summary>
    Block Insert(Block block)
    {
        var doc = _box.Document;
        var anchor = TopLevelBlock();
        if (anchor is Paragraph p && new TextRange(p.ContentStart, p.ContentEnd).Text.Trim().Length == 0)
        {
            doc.Blocks.InsertBefore(p, block);
            if (doc.Blocks.LastBlock != p) doc.Blocks.Remove(p);
        }
        else if (anchor is not null) doc.Blocks.InsertAfter(anchor, block);
        else doc.Blocks.Add(block);

        if (block.NextBlock is null) doc.Blocks.Add(NewNormalParagraph());
        return block;
    }

    static Paragraph NewNormalParagraph()
    {
        var p = new Paragraph();
        DocumentMapper.ApplyStyle(p, DocStyle.Normal);
        return p;
    }

    public void InsertCallout(string tone) => Change(() =>
    {
        var callout = (WpfSection)Insert(DocumentMapper.NewCalloutFor(tone));
        _box.CaretPosition = ((Paragraph)callout.Blocks.FirstBlock).ContentStart;
    });

    public void InsertDivider() => Change(() =>
    {
        var divider = Insert(DocumentMapper.NewDivider(Paper));
        if (divider.NextBlock is Paragraph next) _box.CaretPosition = next.ContentStart;
    });

    /// <summary>Everything after it continues at the top of the next page.</summary>
    public void InsertPageBreak() => Change(() =>
    {
        var marker = Insert(DocumentMapper.NewPageBreakMarker(Paper));
        if (marker.NextBlock is Paragraph next) _box.CaretPosition = next.ContentStart;
    });

    public void InsertTable(int rows, int columns) => Change(() =>
    {
        var model = new TableBlock
        {
            Rows = Enumerable.Range(0, Math.Max(1, rows))
                .Select(_ => Enumerable.Range(0, Math.Max(1, columns)).Select(_ => new Core.Document.TableCell()).ToList())
                .ToList(),
        };
        var table = (WpfTable)Insert(DocumentMapper.NewTable(model, Paper));
        if (table.RowGroups[0].Rows[0].Cells[0].Blocks.FirstBlock is Paragraph first) _box.CaretPosition = first.ContentStart;
    });

    // ---------------------------------------------------------------- tables

    public void AddRow(bool below) => TableEdit((table, rowIndex, columnIndex) =>
    {
        var row = new TableRow();
        for (var c = 0; c < table.Columns.Count; c++) row.Cells.Add(DocumentMapper.NewCell([], header: false, Paper));
        table.RowGroups[0].Rows.Insert(below ? rowIndex + 1 : rowIndex, row);
        return (below ? rowIndex + 1 : rowIndex, columnIndex);
    });

    public void AddColumn(bool right) => TableEdit((table, rowIndex, columnIndex) =>
    {
        var at = right ? columnIndex + 1 : columnIndex;
        table.Columns.Insert(at, new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var row in table.RowGroups.SelectMany(g => g.Rows))
            row.Cells.Insert(Math.Min(at, row.Cells.Count), DocumentMapper.NewCell([], header: false, Paper));
        return (rowIndex, at);
    });

    public void DeleteRow() => TableEdit((table, rowIndex, columnIndex) =>
    {
        var rows = table.RowGroups[0].Rows;
        if (rows.Count <= 1) { RemoveTable(table); return null; }
        rows.RemoveAt(rowIndex);
        return (Math.Min(rowIndex, rows.Count - 1), columnIndex);
    });

    public void DeleteColumn() => TableEdit((table, rowIndex, columnIndex) =>
    {
        if (table.Columns.Count <= 1) { RemoveTable(table); return null; }
        table.Columns.RemoveAt(columnIndex);
        foreach (var row in table.RowGroups.SelectMany(g => g.Rows))
            if (columnIndex < row.Cells.Count) row.Cells.RemoveAt(columnIndex);
        return (rowIndex, Math.Min(columnIndex, table.Columns.Count - 1));
    });

    void RemoveTable(WpfTable table)
    {
        var doc = _box.Document;
        var next = table.NextBlock ?? table.PreviousBlock;
        doc.Blocks.Remove(table);
        if (doc.Blocks.Count == 0) doc.Blocks.Add(NewNormalParagraph());
        _box.CaretPosition = (next ?? doc.Blocks.FirstBlock).ContentStart;
    }

    /// <summary>Runs a table change at the caret's cell, then restyles the header row and puts the caret back in a cell.</summary>
    void TableEdit(Func<WpfTable, int, int, (int Row, int Column)?> edit)
    {
        if (CurrentCell is not { Parent: TableRow row } cell || row.Parent is not TableRowGroup group || group.Parent is not WpfTable table) return;
        var rowIndex = group.Rows.IndexOf(row);
        var columnIndex = row.Cells.IndexOf(cell);

        Change(() =>
        {
            if (edit(table, rowIndex, columnIndex) is not { } target) return;
            var rows = table.RowGroups[0].Rows;
            for (var r = 0; r < rows.Count; r++)
                foreach (var c in rows[r].Cells)
                {
                    if (r == 0) { c.Background = DocLook.TableHeader(Paper); c.FontWeight = FontWeights.SemiBold; }
                    else { c.ClearValue(TextElement.BackgroundProperty); c.ClearValue(TextElement.FontWeightProperty); }
                }
            var targetRow = rows[Math.Clamp(target.Row, 0, rows.Count - 1)];
            var targetCell = targetRow.Cells[Math.Clamp(target.Column, 0, targetRow.Cells.Count - 1)];
            _box.CaretPosition = targetCell.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        });
    }

    // ---------------------------------------------------------------- house rules

    void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        for (var node = e.OriginalSource as DependencyObject; node is not null; node = Up(node))
        {
            if (node is TextBlock { Parent: InlineUIContainer box } && DocumentMapper.IsCheckBox(box))
            {
                DocumentMapper.Toggle(box);
                e.Handled = true;
                ContentChanged?.Invoke();
                return;
            }
        }
    }

    static DependencyObject? Up(DependencyObject node) =>
        node is Visual ? VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (e.Key == Key.Enter && ctrl) { InsertPageBreak(); e.Handled = true; return; }
        if (e.Key != Key.Enter || shift) return;
        if (CurrentParagraph is not { } p || !_box.Selection.IsEmpty) return;

        var empty = new TextRange(p.ContentStart, p.ContentEnd).Text.Trim().Length == 0;

        // Enter on an empty last list item or last callout line steps out below it, like other editors
        if (empty && p.Parent is ListItem { Parent: WpfList list } item && item == list.ListItems.LastListItem)
        {
            Change(() =>
            {
                list.ListItems.Remove(item);
                var after = NewNormalParagraph();
                list.SiblingBlocks!.InsertAfter(list, after);
                if (list.ListItems.Count == 0) list.SiblingBlocks.Remove(list);
                _box.CaretPosition = after.ContentStart;
            });
            e.Handled = true;
            return;
        }
        if (empty && p.Parent is WpfSection callout && DocumentMapper.CalloutTone(callout) is not null
            && callout.Blocks.LastBlock == p && callout.Blocks.Count > 1)
        {
            Change(() =>
            {
                callout.Blocks.Remove(p);
                var after = NewNormalParagraph();
                callout.SiblingBlocks!.InsertAfter(callout, after);
                _box.CaretPosition = after.ContentStart;
            });
            e.Handled = true;
            return;
        }

        // Enter at the end of a heading starts ordinary text
        if (DocumentMapper.StyleOf(p) != DocStyle.Normal && p.Parent is FlowDocument or WpfSection or WpfTableCell
            && _box.CaretPosition.GetInsertionPosition(LogicalDirection.Forward).CompareTo(p.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)) >= 0)
        {
            Change(() =>
            {
                var after = NewNormalParagraph();
                p.SiblingBlocks!.InsertAfter(p, after);
                _box.CaretPosition = after.ContentStart;
            });
            e.Handled = true;
        }
    }

    void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        // only words come in: outside formatting, pictures and links would not survive saving anyway
        if (e.SourceDataObject.GetData(DataFormats.UnicodeText, true) is string text)
            e.DataObject = new DataObject(DataFormats.UnicodeText, text);
        else
            e.CancelCommand();
    }

    void OnTextChanged()
    {
        if (_tidying) return;
        Tidy();
        ContentChanged?.Invoke();
    }

    /// <summary>Checklist items always have a box and nothing else does; the text is never left without a paragraph.</summary>
    void Tidy()
    {
        if (_tidying) return;
        _tidying = true;
        try
        {
            var doc = _box.Document;
            foreach (var p in AllParagraphs(doc.Blocks).ToList())
            {
                var inChecklist = p.Parent is ListItem { Parent: WpfList list } item && DocumentMapper.IsChecklist(list) && item.Blocks.FirstBlock == p;
                var first = p.Inlines.FirstInline;
                var hasBox = first is not null && DocumentMapper.IsCheckBox(first);
                if (inChecklist && !hasBox) DocumentMapper.AddCheckBox(p, ticked: false);
                if (!inChecklist)
                    foreach (var stray in p.Inlines.Where(DocumentMapper.IsCheckBox).ToList()) p.Inlines.Remove(stray);
            }
            if (doc.Blocks.Count == 0) doc.Blocks.Add(NewNormalParagraph());
        }
        finally { _tidying = false; }
    }
}
