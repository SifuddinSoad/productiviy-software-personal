using FocusLock.Core.Board;
using FocusLock.Core.Models;

namespace FocusLock.Core.Tests.Board;

public class BoundsTests
{
    [Fact]
    public void Text_height_comes_from_font_size()
    {
        var o = new BoardObject { Kind = ObjKind.Text, W = 300, H = 999, Size = 20 };
        Assert.Equal((300, 30), Bounds.Size(o));
    }

    [Fact]
    public void Table_height_is_32_per_row_and_prompt_is_fixed()
    {
        Assert.Equal((330, 96), Bounds.Size(new BoardObject { Kind = ObjKind.Table, W = 330, Rows = 3 }));
        Assert.Equal((236, 104), Bounds.Size(new BoardObject { Kind = ObjKind.Prompt, W = 236 }));
    }

    [Fact]
    public void Hit_testing_prefers_the_topmost_object_and_ignores_frames()
    {
        var frame = new BoardObject { Id = "f", Kind = ObjKind.Frame, X = 0, Y = 0, W = 400, H = 400 };
        var lower = new BoardObject { Id = "a", Kind = ObjKind.Sticky, X = 10, Y = 10, W = 100, H = 100 };
        var upper = new BoardObject { Id = "b", Kind = ObjKind.Sticky, X = 50, Y = 50, W = 100, H = 100 };
        List<BoardObject> objs = [frame, lower, upper];

        Assert.Equal("b", Bounds.ObjectAt(objs, new Pt(60, 60))!.Id);
        Assert.Equal("a", Bounds.ObjectAt(objs, new Pt(20, 20))!.Id);
        Assert.Null(Bounds.ObjectAt(objs, new Pt(300, 300)));   // only the frame is there
    }

    [Fact]
    public void A_frame_label_is_found_in_the_band_above_the_frame()
    {
        var frame = new BoardObject { Id = "f", Kind = ObjKind.Frame, X = 100, Y = 100, W = 420, H = 300 };
        var sticky = new BoardObject { Id = "s", Kind = ObjKind.Sticky, X = 100, Y = 100, W = 150, H = 92 };
        List<BoardObject> objs = [frame, sticky];

        Assert.Equal("f", Bounds.FrameLabelAt(objs, new Pt(130, 90))!.Id);    // on the label text
        Assert.Equal("f", Bounds.FrameLabelAt(objs, new Pt(500, 81))!.Id);    // anywhere along the top edge
        Assert.Null(Bounds.FrameLabelAt(objs, new Pt(300, 250)));             // inside the frame
        Assert.Null(Bounds.FrameLabelAt(objs, new Pt(130, 60)));              // well above it
        Assert.Null(Bounds.FrameLabelAt(objs, new Pt(90, 90)));               // left of it
    }

    [Fact]
    public void The_topmost_frame_label_wins()
    {
        var lower = new BoardObject { Id = "a", Kind = ObjKind.Frame, X = 0, Y = 100, W = 400, H = 300 };
        var upper = new BoardObject { Id = "b", Kind = ObjKind.Frame, X = 50, Y = 100, W = 400, H = 300 };

        Assert.Equal("b", Bounds.FrameLabelAt([lower, upper], new Pt(60, 90))!.Id);
    }
}

public class ShapeGeometryTests
{
    [Fact]
    public void Rectangle_uses_a_3px_corner()
    {
        var d = ShapeGeometry.Path("rect", null, 100, 60);
        Assert.Equal("M 3 0 H 97 A 3 3 0 0 1 100 3 V 57 A 3 3 0 0 1 97 60 H 3 A 3 3 0 0 1 0 57 V 3 A 3 3 0 0 1 3 0 Z", d);
    }

    [Fact]
    public void Pill_corner_is_half_the_short_side()
    {
        var d = ShapeGeometry.Path("pill", null, 176, 62);
        Assert.StartsWith("M 31 0 H 145", d);
    }

    [Fact]
    public void Rounded_corner_is_capped_at_18()
    {
        Assert.StartsWith("M 18 0", ShapeGeometry.Path("round", null, 176, 74));
        Assert.StartsWith("M 10 0", ShapeGeometry.Path("round", null, 20, 60));
    }

    [Fact]
    public void Ellipse_is_two_arcs()
    {
        Assert.Equal("M 0 30 A 50 30 0 1 1 100 30 A 50 30 0 1 1 0 30 Z", ShapeGeometry.Path("ellipse", null, 100, 60));
    }

    [Theory]
    [InlineData("diamond", "M 50 0 L 100 30 L 50 60 L 0 30 Z")]
    [InlineData("triangle", "M 50 0 L 100 60 L 0 60 Z")]
    [InlineData("hexagon", "M 25 0 L 75 0 L 100 30 L 75 60 L 25 60 L 0 30 Z")]
    public void Polygons_match_the_design(string kind, string expected)
    {
        Assert.Equal(expected, ShapeGeometry.Path(kind, null, 100, 60));
    }

    [Fact]
    public void Custom_polygon_scales_its_normalised_points()
    {
        double[][] pts = [[0, 0], [1, 0.5], [0.5, 1]];
        Assert.Equal("M 0 0 L 100 30 L 50 60 Z", ShapeGeometry.Path("custom", pts, 100, 60));
    }

    [Fact]
    public void Frames_draw_as_rectangles()
    {
        var frame = new BoardObject { Kind = ObjKind.Frame, Shape = "diamond" };
        Assert.StartsWith("M 3 0", ShapeGeometry.Path(frame, 100, 60));
    }

    [Fact]
    public void Narrow_shapes_leave_more_room_for_text()
    {
        Assert.Equal(0.60, ShapeGeometry.TextWidthFactor(new BoardObject { Shape = "triangle" }));
        Assert.Equal(0.92, ShapeGeometry.TextWidthFactor(new BoardObject { Shape = "rect" }));
    }
}

public class StrokePathTests
{
    [Fact]
    public void Empty_stroke_has_no_path()
    {
        Assert.Equal("", StrokePath.Of([]));
    }

    [Fact]
    public void Two_points_draw_a_line()
    {
        Assert.Equal("M 1.0 2.0 L 3.0 4.0", StrokePath.Of([[1, 2], [3, 4]]));
    }

    [Fact]
    public void Longer_strokes_curve_through_midpoints()
    {
        var d = StrokePath.Of([[0, 0], [10, 0], [20, 10], [30, 10]]);
        Assert.Equal("M 0.0 0.0 Q 10.0 0.0 15.0 5.0 Q 20.0 10.0 25.0 10.0 L 30.0 10.0", d);
    }
}
