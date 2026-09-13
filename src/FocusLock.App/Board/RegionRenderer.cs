using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FocusLock.Core.Models;
using CoreRect = FocusLock.Core.Board.Rect;
using Rect = System.Windows.Rect;

namespace FocusLock.App.Board;

/// <summary>
/// Paints one rectangle of a canvas into a bitmap, using the same painter the screen uses, so an
/// exported region looks exactly like the canvas it came from — minus selection and handles.
/// </summary>
public static class RegionRenderer
{
    public static BitmapSource Render(BoardDoc doc, CoreRect region, double scale)
    {
        var width = Math.Max(1, (int)Math.Round(region.W * scale));
        var height = Math.Max(1, (int)Math.Round(region.H * scale));

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(B.Canvas, null, new Rect(0, 0, width, height));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushTransform(new TranslateTransform(-region.X, -region.Y));
            dc.PushClip(new RectangleGeometry(new Rect(region.X, region.Y, region.W, region.H)));

            BoardPainter.Paint(dc, doc, new PaintOptions(PixelsPerDip: 1.0));

            dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
