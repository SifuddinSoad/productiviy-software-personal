using System.IO;
using System.Windows;
using PdfSharp.Fonts;

namespace FocusLock.App.Export;

/// <summary>
/// PDFsharp cannot reach the system fonts on its own, so it is handed the same typeface the app
/// is drawn in. Without this, writing any text into a PDF throws at run time.
/// </summary>
internal sealed class EmbeddedFontResolver : IFontResolver
{
    const string Regular = "SpaceGrotesk-Regular";
    const string Bold = "SpaceGrotesk-Bold";

    static readonly Dictionary<string, byte[]> Loaded = [];
    static bool _installed;

    public static void Install()
    {
        if (_installed) return;
        GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
        _installed = true;
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(isBold ? Bold : Regular);

    public byte[]? GetFont(string faceName)
    {
        if (Loaded.TryGetValue(faceName, out var cached)) return cached;

        var uri = new Uri($"Assets/Fonts/{faceName}.ttf", UriKind.Relative);
        var stream = Application.GetResourceStream(uri)?.Stream;
        if (stream is null) return null;

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        stream.Dispose();

        var bytes = memory.ToArray();
        Loaded[faceName] = bytes;
        return bytes;
    }
}
