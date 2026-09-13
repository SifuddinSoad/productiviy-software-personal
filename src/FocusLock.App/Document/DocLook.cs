using System.Windows;
using System.Windows.Media;
using FocusLock.App.Theme;
using FocusLock.Core.Document;
using Fonts = FocusLock.App.Board.Fonts;

namespace FocusLock.App.Document;

/// <summary>How a document looks: page geometry, paragraph styles and block colours, in one place.</summary>
internal static class DocLook
{
    /// <summary>WPF lays out in 1/96 inch; the document is specified in points.</summary>
    public const double DipPerPoint = 96.0 / 72.0;

    public const double MarginPt = 56;
    public const double HeaderBaselinePt = 32;
    public const double FooterBaselinePt = 26;   // from the bottom edge

    public static double Dip(double points) => points * DipPerPoint;

    public static (double W, double H) PageDip(bool landscape) =>
        landscape ? (Dip(842), Dip(595)) : (Dip(595), Dip(842));

    public static double TextWidthDip(bool landscape) => PageDip(landscape).W - 2 * Dip(MarginPt);

    public static FontFamily TextFont => Fonts.Sans;

    public static double StyleSizePt(string style) => style switch
    {
        DocStyle.Title => 26,
        DocStyle.H1 => 20,
        DocStyle.H2 => 15,
        _ => 11,
    };

    public static FontWeight StyleWeight(string style) => style switch
    {
        DocStyle.Title => FontWeights.Bold,
        DocStyle.H1 or DocStyle.H2 => FontWeights.SemiBold,
        _ => FontWeights.Normal,
    };

    public static Thickness StyleMargin(string style) => style switch
    {
        DocStyle.Title => new Thickness(0, 0, 0, Dip(10)),
        DocStyle.H1 => new Thickness(0, Dip(12), 0, Dip(5)),
        DocStyle.H2 => new Thickness(0, Dip(9), 0, Dip(4)),
        _ => new Thickness(0, 0, 0, Dip(6)),
    };

    public static readonly Thickness ListItemMargin = new(0, 0, 0, Dip(2));

    public static bool PaperIsLight(string paper) => DocOps.InkFor(paper) == DocOps.InkOnLight;

    public static Brush Ink(string paper) => HexBrush.FromHex(DocOps.InkFor(paper));

    public static Brush Muted(string paper) => HexBrush.FromHex(PaperIsLight(paper) ? "#7f8489" : "#9aa0a6");

    public static Brush Rule(string paper) => HexBrush.FromHex(PaperIsLight(paper) ? "#d5d7db" : "#3a3f45");

    public static Brush TableHeader(string paper) => HexBrush.FromHex(PaperIsLight(paper) ? "#f1f2f4" : "#1f2226");

    public static (Brush Fill, Brush Edge) Callout(string tone) => tone switch
    {
        CalloutTone.Important => (HexBrush.FromHex("#faeeda"), HexBrush.FromHex("#ba7517")),
        CalloutTone.Tip => (HexBrush.FromHex("#eaf3de"), HexBrush.FromHex("#639922")),
        _ => (HexBrush.FromHex("#e6f1fb"), HexBrush.FromHex("#378add")),
    };

    /// <summary>Paper colours offered in Page setup.</summary>
    public static readonly (string Name, string Hex)[] Papers =
    [
        ("White", "#ffffff"), ("Cream", "#fbf7ee"), ("Grey", "#f3f4f6"), ("Mint", "#eef7f1"), ("Sky", "#eef4fb"), ("Dark", "#121315"),
    ];

    public static readonly string[] TextColors = ["#17181a", "#7f8489", "#c0392b", "#d35400", "#b7950b", "#1e8449", "#1f6fb2", "#7d3c98"];
    public static readonly string[] Highlights = ["#fff2a8", "#ffd9b3", "#d4f5d0", "#d6e9ff", "#f6d6f0"];

    // Material Symbols codepoints for the checklist box
    public const string BoxEmpty = "";
    public const string BoxTicked = "";
}
