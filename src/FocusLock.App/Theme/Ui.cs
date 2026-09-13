using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace FocusLock.App.Theme;

/// <summary>Attached properties that let one button/input template cover the design's many variants.</summary>
public static class Ui
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(Ui), new FrameworkPropertyMetadata(default(CornerRadius)));
    public static CornerRadius GetCornerRadius(DependencyObject d) => (CornerRadius)d.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject d, CornerRadius v) => d.SetValue(CornerRadiusProperty, v);

    public static readonly DependencyProperty HoverBackgroundProperty = DependencyProperty.RegisterAttached(
        "HoverBackground", typeof(Brush), typeof(Ui), new FrameworkPropertyMetadata(null));
    public static Brush? GetHoverBackground(DependencyObject d) => (Brush?)d.GetValue(HoverBackgroundProperty);
    public static void SetHoverBackground(DependencyObject d, Brush? v) => d.SetValue(HoverBackgroundProperty, v);

    public static readonly DependencyProperty HoverForegroundProperty = DependencyProperty.RegisterAttached(
        "HoverForeground", typeof(Brush), typeof(Ui), new FrameworkPropertyMetadata(null));
    public static Brush? GetHoverForeground(DependencyObject d) => (Brush?)d.GetValue(HoverForegroundProperty);
    public static void SetHoverForeground(DependencyObject d, Brush? v) => d.SetValue(HoverForegroundProperty, v);

    public static readonly DependencyProperty HoverBorderBrushProperty = DependencyProperty.RegisterAttached(
        "HoverBorderBrush", typeof(Brush), typeof(Ui), new FrameworkPropertyMetadata(null));
    public static Brush? GetHoverBorderBrush(DependencyObject d) => (Brush?)d.GetValue(HoverBorderBrushProperty);
    public static void SetHoverBorderBrush(DependencyObject d, Brush? v) => d.SetValue(HoverBorderBrushProperty, v);

    public static readonly DependencyProperty FocusBorderBrushProperty = DependencyProperty.RegisterAttached(
        "FocusBorderBrush", typeof(Brush), typeof(Ui), new FrameworkPropertyMetadata(null));
    public static Brush? GetFocusBorderBrush(DependencyObject d) => (Brush?)d.GetValue(FocusBorderBrushProperty);
    public static void SetFocusBorderBrush(DependencyObject d, Brush? v) => d.SetValue(FocusBorderBrushProperty, v);

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(""));
    public static string GetPlaceholder(DependencyObject d) => (string)d.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject d, string v) => d.SetValue(PlaceholderProperty, v);
}

/// <summary>First non-null value; used so a missing hover colour falls back to the normal one.</summary>
public sealed class FirstNonNullConverter : IMultiValueConverter
{
    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.FirstOrDefault(v => v is not null && v != DependencyProperty.UnsetValue);

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class HexBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && !string.IsNullOrEmpty(s) ? HexBrush.FromHex(s) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public static class HexBrush
{
    static readonly Dictionary<string, SolidColorBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Accepts #rgb, #rrggbb and CSS-style #rrggbbaa.</summary>
    public static SolidColorBrush FromHex(string hex)
    {
        if (Cache.TryGetValue(hex, out var cached)) return cached;
        var h = hex.TrimStart('#');
        if (h.Length == 3) h = string.Concat(h.Select(c => $"{c}{c}"));
        byte P(int i) => byte.Parse(h.AsSpan(i, 2), NumberStyles.HexNumber);
        var color = h.Length == 8
            ? Color.FromArgb(P(6), P(0), P(2), P(4))
            : Color.FromRgb(P(0), P(2), P(4));
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Cache[hex] = brush;
        return brush;
    }
}
