using System.IO;
using FocusLock.App.Board;
using FocusLock.App.Document;
using FocusLock.Core.Export;
using FocusLock.Core.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using CoreRect = FocusLock.Core.Board.Rect;

namespace FocusLock.App.Export;

/// <summary>
/// Writes the session's A4 pages with every section and text box where the user put it, text running
/// on to the following pages, and the header and page numbers when they are on. Pictures are drawn
/// from the canvases as they stand right now, not from anything stored.
/// </summary>
public static class PdfExporter
{
    /// <summary>Pixels per printed point, so pictures stay sharp on screen and in print.</summary>
    const double Oversample = 2.0;

    static readonly XColor DarkPage = XColor.FromArgb(0x12, 0x13, 0x15);
    static readonly XColor DarkText = XColor.FromArgb(0xe9, 0xe9, 0xe7);
    static readonly XColor LightText = XColor.FromArgb(0x17, 0x18, 0x1a);
    static readonly XColor DarkMuted = XColor.FromArgb(0x9a, 0xa0, 0xa6);
    static readonly XColor LightMuted = XColor.FromArgb(0x7f, 0x84, 0x89);

    /// <summary>Header baseline from the top, and page-number baseline from the bottom, in points.</summary>
    public const double HeaderBaseline = 26;
    public const double FooterBaseline = 18;

    /// <returns>How many sections and text boxes were drawn; nothing is written when that is zero.</returns>
    public static int Write(Session session, string path)
    {
        EmbeddedFontResolver.Install();
        PageLayout.Complete(session);
        TextFlow.Settle(session);
        var texts = LayOutTexts(session);
        var plans = session.Plans.ToDictionary(p => p.Id);
        var titles = session.PdfTitles;

        using var document = new PdfDocument();
        document.Info.Title = session.Name;
        document.Info.Creator = "Focus Mood";

        var drawn = 0;
        for (var index = 0; index < session.Pages.Count; index++)
        {
            var sheet = session.Pages[index];
            var (width, height) = PageLayout.SizeOf(sheet);
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(width);
            page.Height = XUnit.FromPoint(height);

            using var gfx = XGraphics.FromPdfPage(page);
            if (!session.PdfLight) gfx.DrawRectangle(new XSolidBrush(DarkPage), 0, 0, width, height);
            var captionBrush = new XSolidBrush(session.PdfLight ? LightText : DarkText);
            var captionFont = new XFont("Space Grotesk", 10, XFontStyleEx.Regular);

            foreach (var item in session.Extracts.Where(e => e.PageId == sheet.Id))
            {
                if (!plans.TryGetValue(item.PlanId, out var plan)) continue;   // its plan was deleted

                var top = item.PageY;
                if (titles)
                {
                    if (!string.IsNullOrWhiteSpace(item.Name))
                        gfx.DrawString(item.Name, captionFont, captionBrush, new XPoint(item.PageX, top + 13));
                    top += PageLayout.CaptionHeight;
                }

                var pictureHeight = item.PageW * PageLayout.AspectOf(item);
                var pixelsPerUnit = item.W > 0 ? item.PageW / item.W * Oversample : Oversample;
                var bitmap = RegionRenderer.Render(plan.Doc, new CoreRect(item.X, item.Y, item.W, item.H), pixelsPerUnit);
                using var stream = new MemoryStream(RegionRenderer.EncodePng(bitmap));
                using var image = XImage.FromStream(stream);
                gfx.DrawImage(image, item.PageX, top, item.PageW, pictureHeight);
                drawn++;
            }

            foreach (var fragment in texts.Where(f => f.PageIndex == index))
            {
                PdfDocumentWriter.DrawFragment(gfx, fragment);
                drawn++;
            }

            DrawHeaderAndNumber(gfx, session, index, width, height);
        }

        if (drawn == 0) return 0;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        document.Save(path);
        return drawn;
    }

    /// <summary>Lays out every text box, first adding any pages its text runs on to.</summary>
    static List<TextFragment> LayOutTexts(Session session)
    {
        var deck = new PageDeck(session);
        foreach (var text in session.TextItems)
        {
            var count = TextFlow.Layout(session, text).Count;
            if (count > 1) deck.EnsurePagesAfter(text.PageId, count - 1);
        }
        // pages may have been added, which can change nothing above them but is cheap to lay out again
        return session.TextItems.SelectMany(t => TextFlow.Layout(session, t)).ToList();
    }

    static void DrawHeaderAndNumber(XGraphics gfx, Session session, int index, double width, double height)
    {
        if (!session.PdfHeader && !session.PdfPageNumbers) return;
        var font = new XFont("Space Grotesk", 8.5, XFontStyleEx.Regular);
        var brush = new XSolidBrush(session.PdfLight ? LightMuted : DarkMuted);

        if (session.PdfHeader)
            gfx.DrawString(HeaderText(session), font, brush, new XPoint(PageLayout.Margin, HeaderBaseline), XStringFormats.BaseLineLeft);
        if (session.PdfPageNumbers)
            gfx.DrawString(PageNumberText(index, session.Pages.Count), font, brush,
                new XPoint(width - PageLayout.Margin, height - FooterBaseline), XStringFormats.BaseLineRight);
    }

    public static string HeaderText(Session session) =>
        string.IsNullOrWhiteSpace(session.PdfHeaderText) ? session.Name : session.PdfHeaderText;

    public static string PageNumberText(int index, int count) => $"Page {index + 1} of {count}";
}
