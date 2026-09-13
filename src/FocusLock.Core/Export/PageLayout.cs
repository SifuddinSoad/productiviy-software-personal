namespace FocusLock.Core.Export;

/// <param name="Width">Page width in points.</param>
/// <param name="Height">Page height in points.</param>
/// <param name="Scale">How much the region is shrunk to fit; 1 means printed at its own size.</param>
public readonly record struct PageSize(double Width, double Height, double Scale);

/// <summary>
/// Each page is cut to the region on it: a small diagram gets a small page, and only something
/// larger than A4 is scaled down to fit a full one. Nothing is padded out to a fixed paper size.
/// </summary>
public static class PageLayout
{
    /// <summary>Canvas units are screen pixels at 96 dpi; PDF points are 72 to the inch.</summary>
    public const double PointsPerUnit = 72.0 / 96.0;

    public const double Margin = 18;
    public const double A4Short = 595;
    public const double A4Long = 842;

    /// <summary>Below this a page looks silly, so tiny regions still get a small card.</summary>
    public const double MinSide = 144;

    public const double CaptionHeight = 22;

    public static PageSize PageFor(double regionWidth, double regionHeight, bool caption = false)
    {
        var contentWidth = Math.Max(1, regionWidth) * PointsPerUnit;
        var contentHeight = Math.Max(1, regionHeight) * PointsPerUnit;

        // a wide region gets a landscape ceiling, a tall one portrait
        var wide = contentWidth >= contentHeight;
        var maxWidth = wide ? A4Long : A4Short;
        var maxHeight = wide ? A4Short : A4Long;

        var captionSpace = caption ? CaptionHeight : 0;
        var availableWidth = maxWidth - 2 * Margin;
        var availableHeight = maxHeight - 2 * Margin - captionSpace;

        var scale = Math.Min(1, Math.Min(availableWidth / contentWidth, availableHeight / contentHeight));

        return new PageSize(
            Math.Max(MinSide, contentWidth * scale + 2 * Margin),
            Math.Max(MinSide, contentHeight * scale + 2 * Margin + captionSpace),
            scale);
    }
}
