using System.Windows;
using System.Windows.Media;
using FocusLock.App.Theme;

namespace FocusLock.App.Board;

/// <summary>Frozen brushes and pens for the canvas, using the design's colours.</summary>
internal static class B
{
    public static readonly Brush Canvas = HexBrush.FromHex("#121315");
    public static readonly Brush FrameFill = HexBrush.FromHex("#16181b");
    public static readonly Brush FrameStroke = HexBrush.FromHex("#31353a");
    public static readonly Brush FrameLabel = HexBrush.FromHex("#8b9096");
    public static readonly Brush TableFill = HexBrush.FromHex("#1b1d20");
    public static readonly Brush TableHeader = HexBrush.FromHex("#212428");
    public static readonly Brush TableStroke = HexBrush.FromHex("#34383d");
    public static readonly Brush CellStroke = HexBrush.FromHex("#2a2d31");
    public static readonly Brush OnLight = HexBrush.FromHex("#17181a");
    public static readonly Brush Light = HexBrush.FromHex("#e9e9e7");
    public static readonly Brush Connector = HexBrush.FromHex("#c6cad0");
    public static readonly Brush PromptFill = HexBrush.FromHex("#1c1e21");
    public static readonly Brush PromptText = HexBrush.FromHex("#e4e6e8");
    public static readonly Brush PromptLabel = HexBrush.FromHex("#9aa0a6");
    public static readonly Brush Yellow = HexBrush.FromHex("#f2d06b");
    public static readonly Brush Green = HexBrush.FromHex("#8fd18a");
    public static readonly Brush Badge = HexBrush.FromHex("#17181a");
    public static readonly Brush White = Brushes.White;
    public static readonly Brush Shadow = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0));
    public static readonly Brush MarqueeFill = new SolidColorBrush(Color.FromArgb(23, 233, 233, 231));
    public static readonly Brush DraftFill = new SolidColorBrush(Color.FromArgb(15, 233, 233, 231));
    public static readonly Brush PolyFill = new SolidColorBrush(Color.FromArgb(20, 233, 233, 231));
    public static readonly Brush EraserFill = new SolidColorBrush(Color.FromArgb(36, 233, 233, 231));

    public static readonly Pen FramePen = Frozen(new Pen(FrameStroke, 1));
    public static readonly Pen ShapePen = Frozen(new Pen(OnLight, 1.6) { LineJoin = PenLineJoin.Round });
    public static readonly Pen TablePen = Frozen(new Pen(TableStroke, 1));
    public static readonly Pen CellPen = Frozen(new Pen(CellStroke, 1));
    public static readonly Pen PromptPen = Frozen(new Pen(TableStroke, 1));
    public static readonly Pen SelectionPen = Frozen(new Pen(Light, 1.6));
    public static readonly Pen SelectionMemberPen = Frozen(new Pen(Light, 1.3));
    public static readonly Pen MarqueePen = Frozen(new Pen(Light, 1));
    public static readonly Pen EraserPen = Frozen(new Pen(Light, 1.5));

    public static Pen Dashed(Brush brush, double thickness, double on, double off) =>
        Frozen(new Pen(brush, thickness) { DashStyle = new DashStyle([on / thickness, off / thickness], 0) });

    public static Pen Frozen(Pen p)
    {
        p.Freeze();
        return p;
    }

    static B()
    {
        Shadow.Freeze();
        MarqueeFill.Freeze();
        DraftFill.Freeze();
        PolyFill.Freeze();
        EraserFill.Freeze();
    }
}

internal static class Fonts
{
    public static readonly FontFamily Sans = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Space Grotesk");
    public static readonly FontFamily Mono = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#JetBrains Mono");
    public static readonly FontFamily Icons = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Material Symbols Rounded");

    public static Typeface Face(FontFamily family, FontWeight weight) =>
        new(family, FontStyles.Normal, weight, FontStretches.Normal);
}
