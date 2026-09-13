using FocusLock.Core.Board;
using FocusLock.Core.Document;
using FocusLock.Core.Models;

namespace FocusLock.Core.Export;

/// <summary>
/// The PDF is a stack of A4 pages the user arranges by hand. This holds the page geometry and the
/// rules for putting a section somewhere sensible before anyone has moved it: below what is already
/// on the last page, or on a page of its own when it does not fit.
/// </summary>
public static class PageLayout
{
    /// <summary>Canvas units are screen pixels at 96 dpi; PDF points are 72 to the inch.</summary>
    public const double PointsPerUnit = 72.0 / 96.0;

    public const double A4Short = 595;
    public const double A4Long = 842;
    public const double Margin = 18;

    /// <summary>Space between a section and the one placed below it.</summary>
    public const double Gap = 12;

    /// <summary>The band above a picture that holds its name when titles are on.</summary>
    public const double CaptionHeight = 22;

    /// <summary>Narrower than this a section is too small to read or grab.</summary>
    public const double MinWidth = 36;

    public static (double W, double H) SizeOf(PdfPage page) =>
        page.Landscape ? (A4Long, A4Short) : (A4Short, A4Long);

    /// <summary>Height over width of the region; a degenerate region counts as square.</summary>
    public static double AspectOf(ExtractItem item) =>
        item.W > 0 && item.H > 0 ? item.H / item.W : 1;

    public static double BoxHeight(ExtractItem item, double width, bool titles) =>
        width * AspectOf(item) + (titles ? CaptionHeight : 0);

    public static Rect BoxOf(ExtractItem item, bool titles) =>
        new(item.PageX, item.PageY, item.PageW, BoxHeight(item, item.PageW, titles));

    /// <summary>The largest width at which the box still fits inside the margins of a page this size.</summary>
    static double FitWidth(ExtractItem item, double pageW, double pageH, bool titles)
    {
        var byWidth = pageW - 2 * Margin;
        var byHeight = (pageH - 2 * Margin - (titles ? CaptionHeight : 0)) / AspectOf(item);
        return Math.Max(MinWidth, Math.Min(byWidth, byHeight));
    }

    /// <summary>Printed at the region's own size, unless that is bigger than the page allows.</summary>
    public static double NaturalWidth(ExtractItem item, PdfPage page, bool titles)
    {
        var (pw, ph) = SizeOf(page);
        var own = Math.Max(MinWidth, item.W * PointsPerUnit);
        return Math.Min(own, FitWidth(item, pw, ph, titles));
    }

    /// <summary>Keeps the whole box on its page, shrinking it into the margins first if it is too big.</summary>
    public static void Clamp(ExtractItem item, PdfPage page, bool titles)
    {
        var (pw, ph) = SizeOf(page);
        if (item.PageW < MinWidth) item.PageW = MinWidth;
        if (item.PageW > pw || BoxHeight(item, item.PageW, titles) > ph)
            item.PageW = FitWidth(item, pw, ph, titles);

        var h = BoxHeight(item, item.PageW, titles);
        item.PageX = Math.Clamp(item.PageX, 0, Math.Max(0, pw - item.PageW));
        item.PageY = Math.Clamp(item.PageY, 0, Math.Max(0, ph - h));
    }

    /// <summary>Puts a section below everything on the last page, or on a new page if there is no room.</summary>
    public static void Place(Session session, ExtractItem item)
    {
        var titles = session.PdfTitles;

        if (session.Pages.Count > 0)
        {
            var last = session.Pages[^1];
            var (_, ph) = SizeOf(last);
            var width = NaturalWidth(item, last, titles);
            var others = session.Extracts.Where(e => e != item && e.PageId == last.Id).ToList();
            var top = others.Count == 0 ? Margin : others.Max(e => BoxOf(e, titles).Bottom) + Gap;

            if (top + BoxHeight(item, width, titles) <= ph - Margin)
            {
                Put(item, last, Margin, top, width);
                return;
            }
        }

        var page = new PdfPage { Id = Ids.New("pg"), Landscape = item.W > item.H };
        session.Pages.Add(page);
        Put(item, page, Margin, Margin, NaturalWidth(item, page, titles));
    }

    static void Put(ExtractItem item, PdfPage page, double x, double y, double width)
    {
        item.PageId = page.Id;
        item.PageX = x;
        item.PageY = y;
        item.PageW = width;
    }

    /// <summary>
    /// Makes a session's layout whole: at least one page, and every section and text box on a page
    /// that exists. Sessions from before pages had none of this, so their sections are placed in list
    /// order; text written in the old Document mode becomes a text box on a page of its own.
    /// </summary>
    public static void Complete(Session session)
    {
        MoveLegacyDocument(session);

        if (session.Pages.Count == 0 && session.Extracts.Count == 0)
            session.Pages.Add(new PdfPage { Id = Ids.New("pg") });

        var pageIds = session.Pages.Select(p => p.Id).ToHashSet();
        foreach (var item in session.Extracts.Where(e => !pageIds.Contains(e.PageId)).ToList())
        {
            Place(session, item);
            pageIds.Add(item.PageId);
        }
        foreach (var text in session.TextItems.Where(t => !pageIds.Contains(t.PageId)))
        {
            text.PageId = session.Pages[^1].Id;
            Clamp(text, session.Pages[^1]);
        }
    }

    static void MoveLegacyDocument(Session session)
    {
        if (session.LegacyDocument is not { } legacy) return;
        session.LegacyDocument = null;

        session.PdfHeader |= legacy.ShowHeader;
        session.PdfPageNumbers |= legacy.ShowPageNumbers;
        if (session.PdfHeaderText.Length == 0) session.PdfHeaderText = legacy.HeaderText;

        // pictures already live on the pages as sections; only the words need a home
        var blocks = legacy.Blocks.Where(b => b is not SectionBlock).ToList();
        if (Document.DocOps.IsBlank(blocks)) return;

        var page = new PdfPage { Id = Ids.New("pg") };
        session.Pages.Add(page);
        session.TextItems.Add(new TextItem
        {
            Id = Ids.New("t"), PageId = page.Id, PageX = Margin, PageY = Margin, PageW = A4Short - 2 * Margin, Blocks = blocks,
        });
    }

    // ---------------------------------------------------------------- text boxes

    /// <summary>A text box is never narrower than this, nor its top closer to the page's bottom edge.</summary>
    public const double MinTextWidth = 60;
    public const double MinTextRoom = 24;

    /// <summary>Room kept at the top of a page for the header, and at the bottom for page numbers.</summary>
    public const double HeaderBand = 40;
    public const double FooterBand = 34;

    /// <summary>Where text may run on a page: from below the header to above the page numbers, or the margins.</summary>
    public static (double Top, double Bottom) TextArea(Session session, PdfPage page)
    {
        var (_, ph) = SizeOf(page);
        return (session.PdfHeader ? HeaderBand : Margin, ph - (session.PdfPageNumbers ? FooterBand : Margin));
    }

    /// <summary>Keeps a text box's left edge and width on its page and leaves room below its top for at least a line.</summary>
    public static void Clamp(TextItem item, PdfPage page)
    {
        var (pw, ph) = SizeOf(page);
        item.PageW = Math.Clamp(item.PageW, MinTextWidth, pw);
        item.PageX = Math.Clamp(item.PageX, 0, pw - item.PageW);
        item.PageY = Math.Clamp(item.PageY, 0, ph - Margin - MinTextRoom);
    }
}
