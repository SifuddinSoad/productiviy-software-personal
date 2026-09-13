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
/// Everything the document editor does to its <see cref="RichTextBox"/>: styles, lists, blocks,
/// tables, sections, and the house rules (plain-text paste, Enter after a heading, leaving a list
/// or callout, checklist boxes). Structural changes are wrapped in one change so a single Ctrl+Z
/// undoes each of them.
/// </summary>
public sealed class DocumentEditor
{
    readonly RichTextBox _box;
    readonly Func<string, SectionSource?> _sections;
    bool _tidying;

    public DocModel Model { get; }

    /// <summary>The text or structure changed; worth saving soon.</summary>
    public event Action? ContentChanged;

    /// <summary>The caret moved or a section was picked; toolbar and side panel should follow.</summary>
    public event Action? ContextChanged;

    public BlockUIContainer? SelectedSection { get; private set; }

    public DocumentEditor(RichTextBox box, DocModel model, Func<string, SectionSource?> sections)
    {
        _box = box;
        Model = model;
        _sections = sections;

        _box.IsDocumentEnabled = true;   // so checklist boxes and section pictures can be clicked
        _box.AcceptsTab = true;
        _box.AllowDrop = false;
        _box.UndoLimit = 200;
        _box.SpellCheck.IsEnabled = false;

        _box.TextChanged += (_, _) => OnTextChanged();
        _box.SelectionChanged += (_, _) => ContextChanged?.Invoke();
        _box.PreviewKeyDown += OnPreviewKeyDown;
        _box.PreviewMouseLeftButtonDown += OnPreviewMouseDown;
        DataObject.AddPastingHandler(_box, OnPasting);
    }

    // ---------------------------------------------------------------- load and read

    /// <summary>Builds the editor's document from the model. Undo history starts again.</summary>
    public void Load()
    {
        SelectedSection = null;
        var doc = DocumentMapper.Build(Model, _sections, forPrint: false);
        _box.Document = doc;
        _box.Background = HexBrush.FromHex(Model.Paper);
        _box.Foreground = DocLook.Ink(Model.Paper);
        _box.CaretBrush = DocLook.Ink(Model.Paper);
        // RichTextBox ignores the document's page padding, so the margins go on the box instead,
        // corrected once laid out by whatever inset the box adds of its own
        var margin = DocLook.Dip(DocLook.MarginPt);
        doc.PagePadding = new Thickness(0);
        // lines wrap at exactly the printed text width, whatever room the box and its scrollbar leave
        doc.PageWidth = DocLook.TextWidthDip(Model.Landscape);
        _box.Padding = new Thickness(margin, margin, margin, margin);
        _box.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (_box.Document != doc) return;
            var inset = doc.ContentStart.GetInsertionPosition(LogicalDirection.Forward).GetCharacterRect(LogicalDirection.Forward).Left;
            if (double.IsNaN(inset) || double.IsInfinity(inset)) return;
            var extra = inset - margin;
            if (Math.Abs(extra) > 0.5)
                _box.Padding = new Thickness(margin - extra, margin, margin - extra, margin);
        });
        _box.CaretPosition = doc.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        ContextChanged?.Invoke();
    }

    /// <summary>The blocks as they stand, for saving, previewing or printing.</summary>
    public List<DocBlock> Read()
    {
        var blocks = DocumentMapper.Read(_box.Document);
        // text coloured exactly like the paper's own ink is the same as no colour at all
        var ink = DocOps.InkFor(Model.Paper);
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

    public bool SelectionIs(DependencyProperty dp, object value)
    {
        var current = _box.Selection.GetPropertyValue(dp);
        return current != DependencyProperty.UnsetValue && Equals(current, value);
    }

    public bool IsBold => _box.Selection.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight w && w.ToOpenTypeWeight() >= 700;
    public bool IsItalic => SelectionIs(TextElement.FontStyleProperty, FontStyles.Italic);
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

    void AfterCommand()
    {
        ContentChanged?.Invoke();
        ContextChanged?.Invoke();
        _box.Focus();
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
        _box.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, hex is null ? DocLook.Ink(Model.Paper) : HexBrush.FromHex(hex));
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
        if (current == kind)
        {
            // leaving a list: WPF's own toggle takes the paragraphs out
            Change(() =>
            {
                var command = kind == DocList.Number ? EditingCommands.ToggleNumbering : EditingCommands.ToggleBullets;
                if (kind == DocList.Check) RetagListsInSelection(TextMarkerStyle.Disc, null);
                command.Execute(null, _box);
            });
            return;
        }

        Change(() =>
        {
            if (current == DocList.None)
            {
                var command = kind == DocList.Number ? EditingCommands.ToggleNumbering : EditingCommands.ToggleBullets;
                command.Execute(null, _box);
            }
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

    /// <summary>The block directly in the document that holds the caret: a paragraph, list, callout or table.</summary>
    Block? TopLevelBlock()
    {
        if (SelectedSection is not null) return SelectedSection;
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
        if (anchor is Paragraph p && p.Parent is FlowDocument && new TextRange(p.ContentStart, p.ContentEnd).Text.Trim().Length == 0)
        {
            doc.Blocks.InsertBefore(p, block);
            if (block.NextBlock is null) doc.Blocks.Add(new Paragraph());
            else if (block.NextBlock == p && doc.Blocks.LastBlock != p) doc.Blocks.Remove(p);
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
        var divider = Insert(DocumentMapper.NewDivider(Model.Paper));
        if (divider.NextBlock is Paragraph next) _box.CaretPosition = next.ContentStart;
    });

    public void InsertPageBreak() => Change(() =>
    {
        var marker = Insert(DocumentMapper.NewPageBreakMarker(Model.Paper));
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
        var table = (WpfTable)Insert(DocumentMapper.NewTable(model, Model.Paper));
        if (table.RowGroups[0].Rows[0].Cells[0].Blocks.FirstBlock is Paragraph first) _box.CaretPosition = first.ContentStart;
    });

    public void InsertSection(string extractId) => Change(() =>
    {
        var section = new SectionBlock { ExtractId = extractId };
        var block = Insert(new BlockUIContainer(DocumentMapper.SectionVisual(section, _sections(extractId), Model))
        {
            Tag = section,
            Margin = new Thickness(0, DocLook.Dip(4), 0, DocLook.Dip(10)),
        });
        Select((BlockUIContainer)block);
    });

    // ---------------------------------------------------------------- tables

    public void AddRow(bool below) => TableEdit((table, rowIndex, columnIndex) =>
    {
        var group = table.RowGroups[0];
        var row = new TableRow();
        for (var c = 0; c < table.Columns.Count; c++) row.Cells.Add(DocumentMapper.NewCell([], header: false, Model.Paper));
        group.Rows.Insert(below ? rowIndex + 1 : rowIndex, row);
        return (below ? rowIndex + 1 : rowIndex, columnIndex);
    });

    public void AddColumn(bool right) => TableEdit((table, rowIndex, columnIndex) =>
    {
        var at = right ? columnIndex + 1 : columnIndex;
        table.Columns.Insert(at, new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var row in table.RowGroups.SelectMany(g => g.Rows))
            row.Cells.Insert(Math.Min(at, row.Cells.Count), DocumentMapper.NewCell([], header: false, Model.Paper));
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
                    if (r == 0) { c.Background = DocLook.TableHeader(Model.Paper); c.FontWeight = FontWeights.SemiBold; }
                    else { c.ClearValue(TextElement.BackgroundProperty); c.ClearValue(TextElement.FontWeightProperty); }
                }
            var targetRow = rows[Math.Clamp(target.Row, 0, rows.Count - 1)];
            var targetCell = targetRow.Cells[Math.Clamp(target.Column, 0, targetRow.Cells.Count - 1)];
            _box.CaretPosition = targetCell.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        });
    }

    // ---------------------------------------------------------------- sections

    public SectionBlock? SelectedSectionSettings => SelectedSection?.Tag as SectionBlock;

    void Select(BlockUIContainer? section)
    {
        if (SelectedSection?.Child is Border old) old.BorderBrush = Brushes.Transparent;
        SelectedSection = section;
        if (section?.Child is Border frame) frame.BorderBrush = HexBrush.FromHex("#378add");
        ContextChanged?.Invoke();
    }

    public void UpdateSection(Action<SectionBlock> change)
    {
        if (SelectedSection is not { Tag: SectionBlock settings } container) return;
        change(settings);
        var frame = DocumentMapper.SectionVisual(settings, _sections(settings.ExtractId), Model);
        if (frame is Border border) border.BorderBrush = HexBrush.FromHex("#378add");
        container.Child = frame;
        ContentChanged?.Invoke();
        ContextChanged?.Invoke();
    }

    public void RemoveSelectedSection() => Change(() =>
    {
        if (SelectedSection is not { } section) return;
        var doc = _box.Document;
        var next = section.NextBlock ?? section.PreviousBlock;
        doc.Blocks.Remove(section);
        SelectedSection = null;
        if (doc.Blocks.Count == 0) doc.Blocks.Add(NewNormalParagraph());
        _box.CaretPosition = (next ?? doc.Blocks.FirstBlock).ContentStart;
    });

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
            if (node is Border { Parent: BlockUIContainer { Tag: SectionBlock } container })
            {
                Select(container);
                _box.Focus();
                e.Handled = true;
                return;
            }
        }
        if (SelectedSection is not null) Select(null);
    }

    static DependencyObject? Up(DependencyObject node) =>
        node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (SelectedSection is not null && e.Key is Key.Delete or Key.Back)
        {
            RemoveSelectedSection();
            e.Handled = true;
            return;
        }
        if (SelectedSection is not null && e.Key is not (Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift))
            Select(null);

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

    /// <summary>Checklist items always have a box and nothing else does; a document is never left without a paragraph.</summary>
    void Tidy()
    {
        if (_tidying) return;
        _tidying = true;
        try
        {
            var doc = _box.Document;
            foreach (var p in AllParagraphs(doc.Blocks).ToList())
            {
                var inChecklist = p.Parent is ListItem { Parent: WpfList list } && DocumentMapper.IsChecklist(list) && ((ListItem)p.Parent).Blocks.FirstBlock == p;
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
