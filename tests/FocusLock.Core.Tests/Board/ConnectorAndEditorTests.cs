using FocusLock.Core.Board;
using FocusLock.Core.Models;

namespace FocusLock.Core.Tests.Board;

public class ConnectorGeometryTests
{
    static BoardObject Rect(string id, double x, double y, double w = 100, double h = 60) =>
        new() { Id = id, Kind = ObjKind.Shape, Shape = "rect", X = x, Y = y, W = w, H = h };

    [Fact]
    public void Attach_leaves_a_rectangle_through_its_edge()
    {
        var o = Rect("a", 0, 0, 100, 60);            // centre (50,30)
        var p = ConnectorGeometry.Attach(o, 500, 30, 0);   // straight to the right

        Assert.Equal(100, p.X, 3);
        Assert.Equal(30, p.Y, 3);
    }

    [Fact]
    public void Attach_padding_pushes_the_point_further_out()
    {
        var o = Rect("a", 0, 0, 100, 60);
        var p = ConnectorGeometry.Attach(o, 500, 30, 10);

        Assert.Equal(110, p.X, 3);
    }

    [Fact]
    public void Attach_follows_an_ellipse_outline()
    {
        var o = new BoardObject { Id = "e", Kind = ObjKind.Shape, Shape = "ellipse", X = 0, Y = 0, W = 100, H = 60 };
        var p = ConnectorGeometry.Attach(o, 50, -500, 0);   // straight up

        Assert.Equal(50, p.X, 3);
        Assert.Equal(0, p.Y, 3);
    }

    [Fact]
    public void Straight_connector_is_a_single_line_with_one_head()
    {
        var c = new Connector { Id = "c", From = "a", To = "b", Style = "straight", Arrows = "end" };
        var path = ConnectorGeometry.Compute(c, Rect("a", 0, 0), Rect("b", 300, 0));

        Assert.StartsWith("M ", path.Data);
        Assert.Contains(" L ", path.Data);
        Assert.Equal(1, path.Heads.Count(c => c == 'Z'));
        Assert.Null(path.Dash);
    }

    [Fact]
    public void Elbow_connector_uses_horizontal_and_vertical_runs()
    {
        var c = new Connector { Style = "elbow", Arrows = "none" };
        var path = ConnectorGeometry.Compute(c, Rect("a", 0, 0), Rect("b", 400, 200));

        Assert.Contains(" H ", path.Data);
        Assert.Contains(" V ", path.Data);
        Assert.Equal("M 0 0", path.Heads);    // no arrows
    }

    [Fact]
    public void Curved_connector_is_a_bezier()
    {
        var c = new Connector { Style = "curve", Arrows = "both", Dash = true };
        var path = ConnectorGeometry.Compute(c, Rect("a", 0, 0), Rect("b", 400, 0));

        Assert.Contains(" C ", path.Data);
        Assert.Equal(2, path.Heads.Count(c => c == 'Z'));
        Assert.Equal<double[]>([7, 6], path.Dash!);
    }

    [Fact]
    public void Missing_style_defaults_to_curve()
    {
        var path = ConnectorGeometry.Compute(new Connector { Style = "" }, Rect("a", 0, 0), Rect("b", 400, 0));
        Assert.Contains(" C ", path.Data);
    }

    [Fact]
    public void A_free_end_starts_exactly_at_its_point()
    {
        var c = new Connector { From = "", FromPt = [10, 20], To = "b", Style = "straight", Arrows = "none" };

        var path = ConnectorGeometry.Compute(c, null, Rect("b", 300, 300));

        Assert.StartsWith("M 10.0 20.0 L", path.Data);
    }

    [Fact]
    public void Both_ends_can_be_free()
    {
        var c = new Connector { FromPt = [0, 0], ToPt = [100, 50], Style = "straight", Arrows = "none" };

        var path = ConnectorGeometry.Compute(c, null, null);

        Assert.Equal("M 0.0 0.0 L 100.0 50.0", path.Data);
    }

    [Fact]
    public void An_object_end_still_leaves_its_outline_when_the_other_end_is_free()
    {
        var c = new Connector { From = "a", ToPt = [500, 30], Style = "straight", Arrows = "none" };

        var path = ConnectorGeometry.Compute(c, Rect("a", 0, 0, 100, 60), null);

        // leaves the right edge (x = 100) plus the 2 px stand-off, aimed at the free point
        Assert.StartsWith("M 102.0 30.0", path.Data);
    }

    [Fact]
    public void Endpoints_report_centres_for_objects_and_the_point_for_free_ends()
    {
        var c = new Connector { From = "a", ToPt = [400, 90] };

        var (from, to) = ConnectorGeometry.Endpoints(c, Rect("a", 0, 0, 100, 60), null);

        Assert.Equal(new Pt(50, 30), from);
        Assert.Equal(new Pt(400, 90), to);
    }
}

public class EraserTests
{
    static BoardDoc DocWith(params Stroke[] strokes) => new() { Strokes = [.. strokes] };

    int _n;
    string NewId() => "k" + ++_n;

    [Fact]
    public void Rubbing_the_middle_splits_a_stroke_in_two()
    {
        var doc = DocWith(new Stroke { Id = "k0", Pts = [[0, 0], [10, 0], [20, 0], [30, 0], [40, 0]] });

        var result = Eraser.Erase(doc, new Pt(20, 0), 12, NewId);

        Assert.True(result.Changed);
        Assert.Equal(2, result.Strokes.Count);
        Assert.Equal([[0.0, 0.0], [10.0, 0.0]], result.Strokes[0].Pts);
        Assert.Equal([[30.0, 0.0], [40.0, 0.0]], result.Strokes[1].Pts);
    }

    [Fact]
    public void Strokes_out_of_reach_are_untouched()
    {
        var doc = DocWith(new Stroke { Id = "k0", Pts = [[0, 0], [10, 0]] });

        var result = Eraser.Erase(doc, new Pt(500, 500), 34, NewId);

        Assert.False(result.Changed);
        Assert.Same(doc.Strokes, result.Strokes);
    }

    [Fact]
    public void Single_point_leftovers_are_dropped()
    {
        var doc = DocWith(new Stroke { Id = "k0", Pts = [[0, 0], [10, 0], [20, 0]] });

        var result = Eraser.Erase(doc, new Pt(15, 0), 14, NewId);   // erases the last two

        Assert.Empty(result.Strokes);
    }

    [Fact]
    public void Erasing_over_an_object_reports_it()
    {
        var doc = new BoardDoc { Objs = [new BoardObject { Id = "n1", Kind = ObjKind.Sticky, X = 0, Y = 0, W = 100, H = 100 }] };

        var result = Eraser.Erase(doc, new Pt(50, 50), 20, NewId);

        Assert.Equal("n1", result.RemovedObject!.Id);
        Assert.True(result.Changed);
    }
}

public class BoardEditorTests
{
    int _n;
    string NewId() => "o" + ++_n;

    static BoardObject Sticky(string id, double x = 0, double y = 0) =>
        new() { Id = id, Kind = ObjKind.Sticky, X = x, Y = y, W = 150, H = 92, Fill = "#f2d06b" };

    [Fact]
    public void Undo_and_redo_walk_the_history()
    {
        var doc = new BoardDoc();
        var editor = new BoardEditor(doc);

        editor.Add(Sticky("a"));
        editor.Add(Sticky("b"));
        Assert.Equal(2, doc.Objs.Count);

        editor.Undo();
        Assert.Equal(["a"], doc.Objs.Select(o => o.Id));

        editor.Undo();
        Assert.Empty(doc.Objs);
        Assert.False(editor.CanUndo);

        editor.Redo();
        editor.Redo();
        Assert.Equal(["a", "b"], doc.Objs.Select(o => o.Id));
    }

    [Fact]
    public void A_new_change_clears_the_redo_stack()
    {
        var editor = new BoardEditor(new BoardDoc());
        editor.Add(Sticky("a"));
        editor.Undo();
        Assert.True(editor.CanRedo);

        editor.Add(Sticky("b"));

        Assert.False(editor.CanRedo);
    }

    [Fact]
    public void Undo_restores_edited_text()
    {
        var doc = new BoardDoc { Objs = [Sticky("a")] };
        var editor = new BoardEditor(doc);
        doc.Objs[0].Text = "first";

        editor.SetText("a", "second");
        Assert.Equal("second", doc.Objs[0].Text);

        editor.Undo();
        Assert.Equal("first", doc.Objs[0].Text);
    }

    [Fact]
    public void Setting_the_same_text_is_not_an_undo_step()
    {
        var doc = new BoardDoc { Objs = [Sticky("a")] };
        var editor = new BoardEditor(doc);

        editor.SetText("a", "");

        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void Deleting_an_object_also_deletes_its_connectors()
    {
        var doc = new BoardDoc
        {
            Objs = [Sticky("a"), Sticky("b"), Sticky("c")],
            Conns =
            [
                new Connector { Id = "c1", From = "a", To = "b" },
                new Connector { Id = "c2", From = "b", To = "c" },
            ],
        };
        var editor = new BoardEditor(doc);

        editor.Delete(["b"]);

        Assert.Equal(["a", "c"], doc.Objs.Select(o => o.Id));
        Assert.Empty(doc.Conns);
    }

    [Fact]
    public void Duplicate_offsets_copies_by_26()
    {
        var doc = new BoardDoc { Objs = [Sticky("a", 10, 20)] };
        var editor = new BoardEditor(doc);

        var ids = editor.Duplicate(["a"], NewId);

        var copy = doc.Objs.Single(o => o.Id == ids[0]);
        Assert.Equal(36, copy.X);
        Assert.Equal(46, copy.Y);
        Assert.Equal("#f2d06b", copy.Fill);
    }

    [Fact]
    public void Duplicate_makes_a_deep_copy_of_table_cells()
    {
        var table = new BoardObject { Id = "t", Kind = ObjKind.Table, W = 300, Cols = 2, Rows = 1, Cells = ["a", "b"] };
        var doc = new BoardDoc { Objs = [table] };
        var editor = new BoardEditor(doc);

        var ids = editor.Duplicate(["t"], NewId);
        doc.Objs.Single(o => o.Id == ids[0]).Cells![0] = "changed";

        Assert.Equal("a", table.Cells![0]);
    }

    [Fact]
    public void Votes_never_go_below_zero()
    {
        var doc = new BoardDoc { Objs = [Sticky("a")] };
        var editor = new BoardEditor(doc);

        editor.AddVotes(["a"], 2);
        editor.AddVotes(["a"], -5);

        Assert.Equal(0, doc.Objs[0].Votes);
    }

    [Fact]
    public void Align_left_uses_the_leftmost_edge()
    {
        var doc = new BoardDoc { Objs = [Sticky("a", 40, 0), Sticky("b", 10, 0), Sticky("c", 90, 0)] };
        var editor = new BoardEditor(doc);

        editor.AlignLeft(["a", "b", "c"]);

        Assert.All(doc.Objs, o => Assert.Equal(10, o.X));
    }

    [Fact]
    public void Distribute_stacks_with_a_24px_gap()
    {
        var doc = new BoardDoc { Objs = [Sticky("a", 0, 100), Sticky("b", 0, 0), Sticky("c", 0, 500)] };
        var editor = new BoardEditor(doc);

        editor.DistributeVertically(["a", "b", "c"]);

        Assert.Equal(0, doc.Objs.Single(o => o.Id == "b").Y);
        Assert.Equal(116, doc.Objs.Single(o => o.Id == "a").Y);   // 0 + 92 + 24
        Assert.Equal(232, doc.Objs.Single(o => o.Id == "c").Y);
    }

    [Fact]
    public void Objects_and_connectors_delete_together_in_one_undo_step()
    {
        var doc = new BoardDoc
        {
            Objs = [Sticky("a"), Sticky("b"), Sticky("c")],
            Conns =
            [
                new Connector { Id = "c1", From = "a", To = "b" },
                new Connector { Id = "c2", From = "b", To = "c" },
                new Connector { Id = "c3", FromPt = [0, 0], ToPt = [10, 10] },
            ],
        };
        var editor = new BoardEditor(doc);

        editor.Delete(["a"], ["c3"]);

        Assert.Equal(["b", "c"], doc.Objs.Select(o => o.Id));
        Assert.Equal(["c2"], doc.Conns.Select(c => c.Id));   // c1 went with object "a"

        editor.Undo();
        Assert.Equal(3, doc.Objs.Count);
        Assert.Equal(3, doc.Conns.Count);
    }

    [Fact]
    public void Deleting_only_connectors_leaves_objects_alone()
    {
        var doc = new BoardDoc
        {
            Objs = [Sticky("a"), Sticky("b")],
            Conns = [new Connector { Id = "c1", From = "a", To = "b" }, new Connector { Id = "c2", From = "b", To = "a" }],
        };
        var editor = new BoardEditor(doc);

        editor.Delete([], ["c1", "c2"]);

        Assert.Equal(2, doc.Objs.Count);
        Assert.Empty(doc.Conns);
    }

    [Fact]
    public void Erase_result_is_applied_as_one_undo_step()
    {
        var doc = new BoardDoc
        {
            Objs = [Sticky("a")],
            Conns = [new Connector { Id = "c1", From = "a", To = "a" }],
            Strokes = [new Stroke { Id = "k0", Pts = [[0, 0], [10, 0]] }],
        };
        var editor = new BoardEditor(doc);

        editor.ApplyErase(Eraser.Erase(doc, new Pt(50, 50), 20, NewId));

        Assert.Empty(doc.Objs);
        Assert.Empty(doc.Conns);

        editor.Undo();
        Assert.Single(doc.Objs);
        Assert.Single(doc.Conns);
    }
}
