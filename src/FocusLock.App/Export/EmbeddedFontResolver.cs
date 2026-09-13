using System.IO;
using System.Windows;
using PdfSharp.Fonts;

namespace FocusLock.App.Export;

/// <summary>
/// PDFsharp cannot reach the system fonts on its own, so it is handed the same typefaces the app
/// is drawn in. Without this, writing any text into a PDF throws at run time.
///
/// A family name that is already one of the embedded file names ("SpaceGrotesk-Medium") picks that
/// exact face; that is how the document writer asks for the face WPF actually used. Anything else
/// gets Space Grotesk, regular or bold.
/// </summary>
internal sealed class EmbeddedFontResolver : IFontResolver
{
    const string Regular = "SpaceGrotesk-Regular";
    const string Bold = "SpaceGrotesk-Bold";

    public static readonly HashSet<string> Faces =
    [
        "SpaceGrotesk-Regular", "SpaceGrotesk-Medium", "SpaceGrotesk-Bold",
        "JetBrainsMono-Regular", "JetBrainsMono-Medium", "JetBrainsMono-SemiBold",
        "MaterialSymbolsRounded",
    ];

    static readonly Dictionary<string, byte[]> Loaded = [];
    static bool _installed;

    public static void Install()
    {
        if (_installed) return;
        GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
        _installed = true;
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        if (Faces.Contains(familyName))
            return new FontResolverInfo(familyName, mustSimulateBold: isBold, mustSimulateItalic: isItalic);
        return new FontResolverInfo(isBold ? Bold : Regular, mustSimulateBold: false, mustSimulateItalic: isItalic);
    }

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
