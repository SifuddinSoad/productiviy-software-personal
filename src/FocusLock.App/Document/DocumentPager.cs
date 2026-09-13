using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using FocusLock.Core.Document;

namespace FocusLock.App.Document;

/// <summary>Where a page after the first starts: which unit (see <see cref="DocumentMapper.Units"/>) and how far into its text.</summary>
public readonly record struct PageStart(int Page, int Unit, int TextOffset);

/// <summary>
/// Lays a document out on A4 pages the way it will be printed, with the header and page numbers
/// drawn into the margins. Preview shows these pages and the PDF is written from them.
/// </summary>
public sealed class DocumentPager : DocumentPaginator
{
    readonly DynamicDocumentPaginator _inner;
    readonly DocModel _model;
    readonly string _header;

    public FlowDocument Flow { get; }

    DocumentPager(FlowDocument flow, DocModel model, string header)
    {
        Flow = flow;
        _model = model;
        _header = header;
        _inner = (DynamicDocumentPaginator)((IDocumentPaginatorSource)flow).DocumentPaginator;
        _inner.PageSize = new Size(flow.PageWidth, flow.PageHeight);
    }

    /// <param name="sessionName">Printed in the header when the document has no header text of its own.</param>
    public static DocumentPager Paginate(DocModel model, Func<string, SectionSource?> sections, string sessionName)
    {
        var flow = DocumentMapper.Build(model, sections, forPrint: true);
        var header = string.IsNullOrWhiteSpace(model.HeaderText) ? sessionName : model.HeaderText.Trim();
        var pager = new DocumentPager(flow, model, header);
        pager._inner.ComputePageCount();
        return pager;
    }

    public override bool IsPageCountValid => _inner.IsPageCountValid;
    public override int PageCount => _inner.PageCount;
    public override Size PageSize { get => _inner.PageSize; set => _inner.PageSize = value; }
    public override IDocumentPaginatorSource Source => _inner.Source;
    public override void ComputePageCount() => _inner.ComputePageCount();

    readonly Dictionary<int, DocumentPage> _framed = [];

    /// <summary>
    /// Pages are framed once and kept: the inner paginator hands back the same page visual every
    /// time, and a visual can only sit inside one frame.
    /// </summary>
    public override DocumentPage GetPage(int pageNumber)
    {
        if (_framed.TryGetValue(pageNumber, out var cached)) return cached;
        var framedPage = Frame(pageNumber);
        _framed[pageNumber] = framedPage;
        return framedPage;
    }

    DocumentPage Frame(int pageNumber)
    {
        var page = _inner.GetPage(pageNumber);
        var size = PageSize;
        var framed = new ContainerVisual();

        var paper = new DrawingVisual();
        using (var dc = paper.RenderOpen())
            dc.DrawRectangle(Theme.HexBrush.FromHex(_model.Paper), null, new Rect(size));
        framed.Children.Add(paper);
        framed.Children.Add(page.Visual);

        var margins = new DrawingVisual();
        using (var dc = margins.RenderOpen())
        {
            var margin = DocLook.Dip(DocLook.MarginPt);
            if (_model.ShowHeader && _header.Length > 0)
            {
                var text = Text(_header, size.Width - 2 * margin);
                dc.DrawText(text, new Point(margin, DocLook.Dip(DocLook.HeaderBaselinePt) - text.Baseline));
            }
            if (_model.ShowPageNumbers)
            {
                var text = Text($"Page {pageNumber + 1} of {PageCount}", size.Width - 2 * margin);
                dc.DrawText(text, new Point(size.Width - margin - text.Width, size.Height - DocLook.Dip(DocLook.FooterBaselinePt) - text.Baseline));
            }
        }
        framed.Children.Add(margins);

        return new DocumentPage(framed, size, new Rect(size), new Rect(size));
    }

    FormattedText Text(string text, double maxWidth)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(DocLook.TextFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            DocLook.Dip(8.5), DocLook.Muted(_model.Paper), 1.0)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        return ft;
    }

    /// <summary>Where pages 2, 3, … begin, as places that can be found again in the editor's own document.</summary>
    public List<PageStart> PageStarts()
    {
        var units = DocumentMapper.Units(Flow);
        var index = new Dictionary<TextElement, int>();
        for (var i = 0; i < units.Count; i++) index[units[i]] = i;

        var starts = new List<PageStart>();
        for (var page = 1; page < PageCount; page++)
        {
            if (_inner.GetPagePosition(_inner.GetPage(page)) is not TextPointer pointer) continue;
            for (DependencyObject? e = pointer.Parent; e is not null; e = (e as TextElement)?.Parent)
            {
                if (e is TextElement te && index.TryGetValue(te, out var unit))
                {
                    var offset = te is Paragraph p ? TextOffset(p, pointer) : 0;
                    starts.Add(new PageStart(page + 1, unit, offset));
                    break;
                }
                if (e is FlowDocument) break;
            }
        }
        return starts;
    }

    public static int TextOffset(Paragraph p, TextPointer pointer) =>
        pointer.CompareTo(p.ContentStart) <= 0 ? 0 : new TextRange(p.ContentStart, pointer).Text.Length;

    /// <summary>The position <paramref name="offset"/> characters into a paragraph, as <see cref="TextOffset"/> counts them.</summary>
    public static TextPointer PointerAt(Paragraph p, int offset)
    {
        var pointer = p.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        while (offset > 0 && pointer.GetNextInsertionPosition(LogicalDirection.Forward) is { } next && next.CompareTo(p.ContentEnd) <= 0)
        {
            if (TextOffset(p, next) > offset) break;
            pointer = next;
            if (TextOffset(p, pointer) >= offset) break;
        }
        return pointer;
    }
}
