using System.IO;
using FocusLock.App.Board;
using FocusLock.Core.Export;
using FocusLock.Core.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using CoreRect = FocusLock.Core.Board.Rect;

namespace FocusLock.App.Export;

/// <summary>
/// Writes the session's A4 pages with every section where the user put it. Pictures are drawn from
/// the canvases as they stand right now, not from anything stored.
/// </summary>
public static class PdfExporter
{
    /// <summary>Pixels per printed point, so pictures stay sharp on screen and in print.</summary>
    const double Oversample = 2.0;

    static readonly XColor DarkPage = XColor.FromArgb(0x12, 0x13, 0x15);
    static readonly XColor DarkText = XColor.FromArgb(0xe9, 0xe9, 0xe7);
    static readonly XColor LightText = XColor.FromArgb(0x17, 0x18, 0x1a);

    /// <returns>How many sections were drawn; nothing is written when that is zero.</returns>
    public static int Write(Session session, string path)
    {
        EmbeddedFontResolver.Install();
        PageLayout.Complete(session);
        var plans = session.Plans.ToDictionary(p => p.Id);
        var titles = session.PdfTitles;

        using var document = new PdfDocument();
        document.Info.Title = session.Name;
        document.Info.Creator = "Focus Mood";

        var drawn = 0;
        foreach (var sheet in session.Pages)
        {
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
        }

        if (drawn == 0) return 0;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        document.Save(path);
        return drawn;
    }
}
