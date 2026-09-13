using FocusLock.Core.Board;
using FocusLock.Core.Models;

namespace FocusLock.Core.Tests.Board;

public class PaletteTests
{
    [Theory]
    [InlineData("#ffffff", true)]
    [InlineData("#f2d06b", true)]    // sticky yellow
    [InlineData("#a8d5c2", true)]    // mint
    [InlineData("#17181a", false)]
    [InlineData("#1b1d20", false)]   // table default
    [InlineData("#7f8489", false)]
    public void Light_backgrounds_are_told_apart_from_dark_ones(string hex, bool light)
    {
        Assert.Equal(light, Palette.IsLight(hex));
    }

    [Fact]
    public void Text_flips_to_stay_readable()
    {
        Assert.Equal(Palette.Dark, Palette.TextOn("#ffffff"));
        Assert.Equal(Palette.Light, Palette.TextOn("#1b1d20"));
    }

    [Fact]
    public void Short_hex_is_accepted()
    {
        Assert.Equal((255, 255, 255), Palette.Rgb("#fff"));
    }

    [Fact]
    public void Shade_darkens_and_lightens_without_leaving_the_range()
    {
        Assert.Equal("#808080", Palette.Shade("#ffffff", 0.5));
        Assert.Equal("#ffffff", Palette.Shade("#ffffff", 2));   // clamped
        Assert.Equal("#000000", Palette.Shade("#000000", 4));
    }
}

public class TextColourTests
{
    [Fact]
    public void Text_colour_is_stored_and_cleared_through_undo()
    {
        var o = new BoardObject { Id = "a", Kind = ObjKind.Sticky, W = 150, H = 92 };
        var doc = new BoardDoc { Objs = [o] };
        var editor = new BoardEditor(doc);

        editor.SetTextColor(["a"], "#aec8e8");
        Assert.Equal("#aec8e8", o.TextColor);

        editor.Undo();
        Assert.Null(doc.Objs[0].TextColor);
    }

    [Fact]
    public void Null_restores_the_automatic_colour()
    {
        var doc = new BoardDoc { Objs = [new BoardObject { Id = "a", TextColor = "#ffffff" }] };
        var editor = new BoardEditor(doc);

        editor.SetTextColor(["a"], null);

        Assert.Null(doc.Objs[0].TextColor);
    }
}
