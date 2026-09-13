using System.IO;
using FocusLock.App.Board;
using FocusLock.Core.Export;
using FocusLock.Core.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using CoreRect = FocusLock.Core.Board.Rect;

namespace FocusLock.App.Export;

/// <summary>
/// Turns the picked regions into a PDF, one region per page, each page cut to the size of what is
/// on it. Pages are drawn from the canvases as they stand right now, not from anything stored.
/// </summary>
public static class PdfExporter
{
    /// <summary>Drawn at twice the page size so the picture does not look soft on screen or in print.</summary>
    const double Oversample = 2.0;

    public static int Write(Session session, string path, bool captions)
    {
        EmbeddedFontResolver.Install();
        var plans = session.Plans.ToDictionary(p => p.Id);

        using var document = new PdfDocument();
        document.Info.Title = session.Name;
        document.Info.Creator = "Focus Mood";

        var written = 0;
        foreach (var item in session.Extracts)
        {
            if (!plans.TryGetValue(item.PlanId, out var plan)) continue;   // its plan was deleted

            var region = new CoreRect(item.X, item.Y, item.W, item.H);
            var layout = PageLayout.PageFor(item.W, item.H, captions);

            var page = document.AddPage();
            page.Width = XUnit.FromPoint(layout.Width);
            page.Height = XUnit.FromPoint(layout.Height);

            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(0x12, 0x13, 0x15)), 0, 0, layout.Width, layout.Height);

            var top = PageLayout.Margin;
            if (captions && !string.IsNullOrWhiteSpace(item.Name))
            {
                var font = new XFont("Space Grotesk", 10, XFontStyleEx.Regular);
                gfx.DrawString(item.Name, font, new XSolidBrush(XColor.FromArgb(0xe9, 0xe9, 0xe7)),
                    new XPoint(PageLayout.Margin, top + 10));
                top += PageLayout.CaptionHeight;
            }

            // render at a multiple of the printed size, then let the PDF scale it back down
            var bitmap = RegionRenderer.Render(plan.Doc, region, PageLayout.PointsPerUnit * layout.Scale * Oversample);
            using var stream = new MemoryStream(RegionRenderer.EncodePng(bitmap));
            using var image = XImage.FromStream(stream);

            gfx.DrawImage(image, PageLayout.Margin, top,
                item.W * PageLayout.PointsPerUnit * layout.Scale,
                item.H * PageLayout.PointsPerUnit * layout.Scale);
            written++;
        }

        if (written == 0) return 0;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        document.Save(path);
        return written;
    }
}
