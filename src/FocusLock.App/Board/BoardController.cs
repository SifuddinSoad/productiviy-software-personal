using FocusLock.Core;
using FocusLock.Core.Board;
using FocusLock.Core.Models;

namespace FocusLock.App.Board;

public static class Tool
{
    public const string Select = "select";
    public const string Hand = "hand";
    public const string Pen = "pen";
    public const string Eraser = "eraser";
    public const string Shape = "shape";
    public const string Connector = "connector";
    public const string Sticky = "sticky";
    public const string Text = "text";
    public const string Frame = "frame";
    public const string Table = "table";
    public const string Prompt = "prompt";
    public const string Vote = "vote";
    public const string Extract = "extract";
}

public sealed record DraftShape(string Kind, double X, double Y, double W, double H);

/// <summary>
/// Canvas interaction state, ported from the design's Whiteboard component: current tool, camera,
/// selection, in-progress drags. Rendering reads this; input handlers drive it.
/// </summary>
public sealed class BoardController(BoardDoc doc, bool readOnly)
{
    Action<Pt, Pt>? _dragMove;
    Action<Pt, Pt>? _dragUp;

    public BoardDoc Doc { get; private set; } = doc;
    public BoardEditor Editor { get; private set; } = new(doc);
    public bool ReadOnly { get; } = readOnly;

    public string CurrentTool { get; private set; } = Tool.Select;
    public string ShapeKind { get; private set; } = "rect";
    public string StickyColor { get; private set; } = "#f2d06b";
    public string ConnStyle { get; private set; } = "curve";
    public bool ConnDash { get; private set; }
    public string ConnArrows { get; private set; } = "end";
    public double EraserSize { get; private set; } = 34;

    public List<string> SelectedIds { get; } = [];
    public List<string> SelectedConnectorIds { get; } = [];

    /// <summary>The connector when exactly one is selected — end handles need an unambiguous target.</summary>
    public string? SelectedConnector => SelectedConnectorIds.Count == 1 ? SelectedConnectorIds[0] : null;
    public string? EditingId { get; set; }
    public int EditingCell { get; set; } = -1;

    public DraftShape? Draft { get; private set; }
    public Rect? Marquee { get; private set; }
    public List<Pt>? PolyPoints { get; private set; }
    public string? ConnectorFrom { get; private set; }
    public Pt? ConnectorFromPoint { get; private set; }
    public Pt MouseWorld { get; private set; }
    public bool IsResizing { get; private set; }
    public bool SpaceHeld { get; set; }

    public double ViewportWidth { get; set; } = 1000;
    public double ViewportHeight { get; set; } = 700;

    public event Action? Changed;
    public event Action<string, int>? EditRequested;

    /// <summary>A region was boxed with the Extract tool; the view model turns it into a PDF item.</summary>
    public event Action<Rect>? ExtractRequested;

    /// <summary>Regions already picked out, outlined on the canvas so you can see what you have.</summary>
    public List<Rect> ExtractOutlines { get; } = [];

    public bool ShowExtractOutlines { get; set; }

    public void Notify() => Changed?.Invoke();

    public void LoadDoc(BoardDoc next)
    {
        Doc = next;
        Editor = new BoardEditor(next);
        SelectedIds.Clear();
        SelectedConnectorIds.Clear();
        EditingId = null;
        EditingCell = -1;
        Draft = null;
        Marquee = null;
        PolyPoints = null;
        ConnectorFrom = null;
        CurrentTool = Tool.Select;
        Notify();
    }

    // ---------- camera ----------

    public Pt ToWorld(Pt screen) => new((screen.X - Doc.Cam.X) / Doc.Cam.Z, (screen.Y - Doc.Cam.Y) / Doc.Cam.Z);

    public void ZoomTo(double z, double? screenX = null, double? screenY = null)
    {
        var px = screenX ?? ViewportWidth / 2;
        var py = screenY ?? ViewportHeight / 2;
        var nz = Math.Clamp(z, 0.2, 3);
        var cam = Doc.Cam;
        cam.X = px - (px - cam.X) * (nz / cam.Z);
        cam.Y = py - (py - cam.Y) * (nz / cam.Z);
        cam.Z = nz;
        Notify();
    }

    public void ZoomIn() => ZoomTo(Doc.Cam.Z * 1.2);
    public void ZoomOut() => ZoomTo(Doc.Cam.Z / 1.2);
    public void ZoomReset() => ZoomTo(1);

    public void ZoomFit()
    {
        if (Doc.Objs.Count == 0)
        {
            Doc.Cam.X = 120;
            Doc.Cam.Y = 60;
            Doc.Cam.Z = 0.9;
            Notify();
            return;
        }

        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var o in Doc.Objs)
        {
            var b = Bounds.Of(o);
            x0 = Math.Min(x0, b.X);
            y0 = Math.Min(y0, b.Y);
            x1 = Math.Max(x1, b.Right);
            y1 = Math.Max(y1, b.Bottom);
        }
        var z = Math.Clamp(Math.Min((ViewportWidth - 96) / (x1 - x0), (ViewportHeight - 150) / (y1 - y0)), 0.2, 1.6);
        Doc.Cam.Z = z;
        Doc.Cam.X = (ViewportWidth - (x1 - x0) * z) / 2 - x0 * z;
        Doc.Cam.Y = (ViewportHeight - (y1 - y0) * z) / 2 - y0 * z;
        Notify();
    }

    public void Pan(double dx, double dy)
    {
        Doc.Cam.X += dx;
        Doc.Cam.Y += dy;
        Notify();
    }

    public void Wheel(double deltaX, double deltaY, bool ctrl, Pt screen)
    {
        if (ctrl)
        {
            ZoomTo(Doc.Cam.Z * (1 - deltaY / 400), screen.X, screen.Y);
            return;
        }
        Pan(-deltaX, -deltaY);
    }

    // ---------- tools ----------

    public void SetTool(string tool)
    {
        if (ReadOnly) return;
        CurrentTool = tool;
        EditingId = null;
        EditingCell = -1;
        ConnectorFrom = null;
        Marquee = null;
        PolyPoints = null;
        if (tool != Tool.Select)
        {
            SelectedIds.Clear();
            SelectedConnectorIds.Clear();
        }
        Notify();
    }

    public void SetShapeKind(string kind)
    {
        ShapeKind = kind;
        PolyPoints = null;
        Notify();
    }

    public void SetStickyColor(string color) { StickyColor = color; Notify(); }
    public void SetConnStyle(string style) { ConnStyle = style; Notify(); }
    public void ToggleConnDash() { ConnDash = !ConnDash; Notify(); }
    public void SetConnArrows(string mode) { ConnArrows = mode; Notify(); }
    public void SetEraserSize(double size) { EraserSize = Math.Clamp(size, 8, 120); Notify(); }

    // ---------- selection ----------

    public IEnumerable<BoardObject> SelectedObjects => Doc.Objs.Where(o => SelectedIds.Contains(o.Id));
    public BoardObject? SingleSelection => SelectedIds.Count == 1 ? Editor.ById(SelectedIds[0]) : null;

    public Rect? SelectionBox()
    {
        var sel = SelectedObjects.ToList();
        if (sel.Count == 0) return null;
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var o in sel)
        {
            var b = Bounds.Of(o);
            x0 = Math.Min(x0, b.X);
            y0 = Math.Min(y0, b.Y);
            x1 = Math.Max(x1, b.Right);
            y1 = Math.Max(y1, b.Bottom);
        }
        return new Rect(x0 - 5, y0 - 5, x1 - x0 + 10, y1 - y0 + 10);
    }

    public void Select(IEnumerable<string> ids, IEnumerable<string>? connectorIds = null)
    {
        SelectedIds.Clear();
        SelectedIds.AddRange(ids);
        SelectedConnectorIds.Clear();
        if (connectorIds is not null) SelectedConnectorIds.AddRange(connectorIds);
        Notify();
    }

    public void SelectAll() => Select(Doc.Objs.Select(o => o.Id), Doc.Conns.Select(c => c.Id));

    public void Deselect()
    {
        SelectedIds.Clear();
        SelectedConnectorIds.Clear();
        EditingId = null;
        EditingCell = -1;
        Notify();
    }

    /// <summary>Shift keeps the rest of the selection and toggles this one.</summary>
    public void SelectConnector(string id, bool add = false)
    {
        if (add)
        {
            if (!SelectedConnectorIds.Remove(id)) SelectedConnectorIds.Add(id);
        }
        else
        {
            SelectedIds.Clear();
            SelectedConnectorIds.Clear();
            SelectedConnectorIds.Add(id);
        }
        Notify();
    }

    // ---------- commands ----------

    public void DeleteSelection()
    {
        if (ReadOnly) return;
        Editor.Delete([.. SelectedIds], [.. SelectedConnectorIds]);
        SelectedIds.Clear();
        SelectedConnectorIds.Clear();
        Notify();
    }

    public void DuplicateSelection()
    {
        if (ReadOnly || SelectedIds.Count == 0) return;
        var ids = Editor.Duplicate([.. SelectedIds], () => Ids.New());
        Select(ids);
    }

    public void Undo() { if (!ReadOnly) { Editor.Undo(); Deselect(); } }
    public void Redo() { if (!ReadOnly) { Editor.Redo(); Deselect(); } }

    public void SetFill(string color)
    {
        if (ReadOnly || SelectedIds.Count == 0) return;
        Editor.SetFill([.. SelectedIds], color);
        Notify();
    }

    /// <summary>Null goes back to the automatic colour that follows the fill.</summary>
    public void SetTextColor(string? color)
    {
        if (ReadOnly || SelectedIds.Count == 0) return;
        Editor.SetTextColor([.. SelectedIds], color);
        Notify();
    }

    public void AddVotes(int delta)
    {
        if (ReadOnly || SelectedIds.Count == 0) return;
        Editor.AddVotes([.. SelectedIds], delta);
        Notify();
    }

    public void Align(string what)
    {
        if (ReadOnly || SelectedIds.Count == 0) return;
        List<string> ids = [.. SelectedIds];
        switch (what)
        {
            case "left": Editor.AlignLeft(ids); break;
            case "top": Editor.AlignTop(ids); break;
            case "center": Editor.AlignCenterHorizontally(ids); break;
            case "distribute": Editor.DistributeVertically(ids); break;
        }
        Notify();
    }

    public IEnumerable<Connector> SelectedConnectors =>
        Doc.Conns.Where(c => SelectedConnectorIds.Contains(c.Id));

    public void SetConnectorStyle(string style) => EditSelectedConnectors(c => c.Style = style);

    public void SetConnectorArrows(string mode) => EditSelectedConnectors(c => c.Arrows = mode);

    /// <summary>Toggles as one: if any selected line is solid they all become dashed.</summary>
    public void ToggleConnectorDash()
    {
        var dash = SelectedConnectors.Any(c => !c.Dash);
        EditSelectedConnectors(c => c.Dash = dash);
    }

    void EditSelectedConnectors(Action<Connector> apply)
    {
        if (ReadOnly || SelectedConnectorIds.Count == 0) return;
        Editor.Snapshot();
        foreach (var c in SelectedConnectors.ToList()) apply(c);
        Notify();
    }

    public void CommitText(string id, string text)
    {
        EditingId = null;
        EditingCell = -1;
        if (!ReadOnly) Editor.SetText(id, text);
        Notify();
    }

    public void CommitCell(string id, int index, string text)
    {
        EditingId = null;
        EditingCell = -1;
        if (!ReadOnly) Editor.SetCell(id, index, text);
        Notify();
    }

    public void BeginEdit(string id, int cell = -1)
    {
        if (ReadOnly) return;
        EditingId = id;
        EditingCell = cell;
        EditRequested?.Invoke(id, cell);
        Notify();
    }

    // ---------- pointer ----------

    public void PointerDown(Pt screen, bool middleButton, bool shift)
    {
        var world = ToWorld(screen);
        MouseWorld = world;

        if (middleButton || CurrentTool == Tool.Hand || SpaceHeld || ReadOnly)
        {
            StartPan(screen);
            return;
        }

        // resize handles win over everything else
        if (SingleSelection is { } single && HandleAt(screen) is { } dir)
        {
            StartResize(single, dir, world);
            return;
        }

        // ... as do the end handles of a selected connector
        if (CurrentTool == Tool.Select && ConnectorEndAt(screen) is { } connectorEnd)
        {
            StartConnectorEndDrag(connectorEnd);
            return;
        }

        switch (CurrentTool)
        {
            case Tool.Pen: StartPen(world); return;
            case Tool.Eraser: StartErase(world); return;
            case Tool.Shape when ShapeKind == "custom": AddPolygonPoint(world); return;
            case Tool.Shape:
            case Tool.Frame:
            case Tool.Extract: StartDraft(world, CurrentTool); return;
            case Tool.Sticky: PlaceSticky(world); return;
            case Tool.Text: PlaceText(world); return;
            case Tool.Table: PlaceTable(world); return;
            case Tool.Prompt: PlacePrompt(world); return;
        }

        var hit = Bounds.ObjectAt(Doc.Objs, world);

        if (CurrentTool == Tool.Vote)
        {
            if (hit is not null)
            {
                Editor.AddVotes([hit.Id], 1);
                Notify();
            }
            return;
        }

        if (CurrentTool == Tool.Connector)
        {
            StartConnector(hit, world);
            return;
        }

        if (CurrentTool != Tool.Select) return;

        if (hit is null)
        {
            if (ConnectorHitTest(world) is { } conn)
            {
                SelectConnector(conn, shift);
                return;
            }
            StartMarquee(world, shift);
            return;
        }

        StartMoveSelection(hit, world, shift);
    }

    public void PointerMove(Pt screen)
    {
        var world = ToWorld(screen);
        MouseWorld = world;
        if (_dragMove is { } move)
        {
            move(world, screen);
            return;
        }
        if (CurrentTool == Tool.Eraser || ConnectorFrom is not null || PolyPoints is { Count: > 0 })
            Notify();
    }

    public void PointerUp(Pt screen)
    {
        var world = ToWorld(screen);
        var up = _dragUp;
        _dragMove = null;
        _dragUp = null;
        up?.Invoke(world, screen);
    }

    void Drag(Action<Pt, Pt> move, Action<Pt, Pt>? up = null)
    {
        _dragMove = move;
        _dragUp = up;
    }

    void StartPan(Pt screen)
    {
        var start = screen;
        var cam0 = (Doc.Cam.X, Doc.Cam.Y);
        Drag((_, s) =>
        {
            Doc.Cam.X = cam0.X + (s.X - start.X);
            Doc.Cam.Y = cam0.Y + (s.Y - start.Y);
            Notify();
        });
    }

    void StartPen(Pt world)
    {
        Editor.Snapshot();
        var stroke = new Stroke { Id = Ids.New("k"), Pts = [[world.X, world.Y]], Color = "#e9e9e7", W = 2.4 };
        Doc.Strokes.Add(stroke);
        Notify();
        Drag((w, _) =>
        {
            stroke.Pts.Add([w.X, w.Y]);
            Notify();
        });
    }

    void StartErase(Pt world)
    {
        EraseAt(world);
        Drag((w, _) => EraseAt(w));
    }

    void EraseAt(Pt world)
    {
        var result = Eraser.Erase(Doc, world, EraserSize, () => Ids.New("k"));
        if (result.Changed) Editor.ApplyErase(result);
        Notify();
    }

    void AddPolygonPoint(Pt world)
    {
        PolyPoints ??= [];
        if (PolyPoints.Count > 2)
        {
            var first = PolyPoints[0];
            if (Math.Sqrt(Math.Pow(first.X - world.X, 2) + Math.Pow(first.Y - world.Y, 2)) < 14 / Doc.Cam.Z)
            {
                ClosePolygon();
                return;
            }
        }
        PolyPoints.Add(world);
        Notify();
    }

    public void ClosePolygon()
    {
        var pts = PolyPoints;
        PolyPoints = null;
        if (pts is null || pts.Count < 3)
        {
            Notify();
            return;
        }

        var x0 = pts.Min(p => p.X);
        var y0 = pts.Min(p => p.Y);
        var w = Math.Max(24, pts.Max(p => p.X) - x0);
        var h = Math.Max(24, pts.Max(p => p.Y) - y0);
        var norm = pts.Select(p => new[] { (p.X - x0) / w, (p.Y - y0) / h }).ToList();

        AddObject(new BoardObject
        {
            Id = Ids.New(), Kind = ObjKind.Shape, Shape = "custom", Pts = norm,
            X = x0, Y = y0, W = w, H = h, Text = "", Fill = "#ffffff",
        });
    }

    void StartDraft(Pt start, string tool)
    {
        Draft = new DraftShape(tool, start.X, start.Y, 0, 0);
        Notify();
        Drag((w, _) =>
        {
            Draft = new DraftShape(tool, Math.Min(start.X, w.X), Math.Min(start.Y, w.Y),
                Math.Abs(w.X - start.X), Math.Abs(w.Y - start.Y));
            Notify();
        }, (_, _) =>
        {
            var d = Draft;
            Draft = null;
            if (d is null) return;

            if (tool == Tool.Extract)
            {
                // a stray click should not add an empty page
                if (d.W >= 20 && d.H >= 20)
                    ExtractRequested?.Invoke(new Rect(Math.Round(d.X), Math.Round(d.Y), Math.Round(d.W), Math.Round(d.H)));
                CurrentTool = Tool.Select;
                Notify();
                return;
            }

            var w = d.W < 16 ? (tool == Tool.Frame ? 420 : 170) : d.W;
            var h = d.H < 16 ? (tool == Tool.Frame ? 300 : 90) : d.H;
            if (tool == Tool.Frame)
                AddObject(new BoardObject
                {
                    Id = Ids.New(), Kind = ObjKind.Frame, X = Math.Round(d.X), Y = Math.Round(d.Y),
                    W = Math.Round(w), H = Math.Round(h), Text = "New section",
                });
            else
                AddObject(new BoardObject
                {
                    Id = Ids.New(), Kind = ObjKind.Shape, Shape = ShapeKind,
                    X = Math.Round(d.X), Y = Math.Round(d.Y), W = Math.Round(w), H = Math.Round(h),
                    Text = "", Fill = "#ffffff",
                });
        });
    }

    void PlaceSticky(Pt p)
    {
        var o = new BoardObject
        {
            Id = Ids.New(), Kind = ObjKind.Sticky, X = Math.Round(p.X - 75), Y = Math.Round(p.Y - 46),
            W = 150, H = 92, Text = "New note", Fill = StickyColor,
        };
        AddObject(o);
        BeginEdit(o.Id);
    }

    void PlaceText(Pt p)
    {
        var o = new BoardObject
        {
            Id = Ids.New(), Kind = ObjKind.Text, X = Math.Round(p.X), Y = Math.Round(p.Y - 12),
            W = 300, Text = "Type something", Size = 20, Weight = 600,
        };
        AddObject(o);
        BeginEdit(o.Id);
    }

    void PlaceTable(Pt p) => AddObject(new BoardObject
    {
        Id = Ids.New(), Kind = ObjKind.Table, X = Math.Round(p.X - 160), Y = Math.Round(p.Y - 40),
        W = 330, Cols = 3, Rows = 3, Cells = ["Column", "Column", "Column", "", "", "", "", "", ""],
    });

    void PlacePrompt(Pt p)
    {
        var o = new BoardObject
        {
            Id = Ids.New(), Kind = ObjKind.Prompt, X = Math.Round(p.X - 118), Y = Math.Round(p.Y - 20),
            W = 236, T = "now", Text = "New prompt…",
        };
        AddObject(o);
        BeginEdit(o.Id);
    }

    void AddObject(BoardObject o)
    {
        Editor.Add(o);
        SelectedIds.Clear();
        SelectedIds.Add(o.Id);
        SelectedConnectorIds.Clear();
        CurrentTool = Tool.Select;
        Notify();
    }

    /// <summary>
    /// Either end may be an object or a bare point, so a connector can be drawn shape-to-shape,
    /// shape-to-empty-space, or freely between two points. Drag to draw; or click one end then
    /// click the other.
    /// </summary>
    void StartConnector(BoardObject? hit, Pt world)
    {
        // Second click of a click-then-click pair.
        if (ConnectorFrom is not null || ConnectorFromPoint is not null)
        {
            if (hit is not null && hit.Id == ConnectorFrom)
            {
                CancelConnector();
                return;
            }
            MakeConnector(ConnectorFrom, ConnectorFromPoint, hit?.Id, hit is null ? world : null);
            return;
        }

        var fromId = hit?.Id;
        Pt? fromPoint = hit is null ? world : null;
        ConnectorFrom = fromId;
        ConnectorFromPoint = fromPoint;
        MouseWorld = world;
        Notify();

        var moved = false;
        Drag((_, _) => { moved = true; Notify(); }, (w, _) =>
        {
            if (!moved)
            {
                // A plain click on empty canvas would otherwise leave a stray pending endpoint.
                if (fromId is null) CancelConnector();
                return;
            }

            var target = Bounds.ObjectAt(Doc.Objs, w);
            if (target is not null && target.Id == fromId)
            {
                CancelConnector();
                return;
            }
            if (target is null && (w - world).Length < 6)
            {
                CancelConnector();
                return;
            }
            MakeConnector(fromId, fromPoint, target?.Id, target is null ? w : null);
        });
    }

    void CancelConnector()
    {
        ConnectorFrom = null;
        ConnectorFromPoint = null;
        Notify();
    }

    void MakeConnector(string? fromId, Pt? fromPoint, string? toId, Pt? toPoint)
    {
        Editor.AddConnector(new Connector
        {
            Id = Ids.New("c"),
            From = fromId ?? "",
            To = toId ?? "",
            FromPt = fromPoint is { } f ? [f.X, f.Y] : null,
            ToPt = toPoint is { } t ? [t.X, t.Y] : null,
            Style = ConnStyle,
            Arrows = ConnArrows,
            Dash = ConnDash,
        });
        ConnectorFrom = null;
        ConnectorFromPoint = null;
        Notify();
    }

    void StartMarquee(Pt start, bool shift)
    {
        EditingId = null;
        if (!shift)
        {
            SelectedIds.Clear();
            SelectedConnectorIds.Clear();
        }
        var baseIds = shift ? SelectedIds.ToList() : [];
        var baseConns = shift ? SelectedConnectorIds.ToList() : [];
        Marquee = new Rect(start.X, start.Y, 0, 0);
        Notify();

        var moved = false;
        Drag((w, _) =>
        {
            moved = Math.Abs(w.X - start.X) + Math.Abs(w.Y - start.Y) > 3;
            Marquee = new Rect(Math.Min(start.X, w.X), Math.Min(start.Y, w.Y),
                Math.Abs(w.X - start.X), Math.Abs(w.Y - start.Y));
            Notify();
        }, (_, _) =>
        {
            var m = Marquee;
            Marquee = null;
            if (m is null || !moved)
            {
                Notify();
                return;
            }
            var hits = Doc.Objs.Where(o => Bounds.Of(o).Intersects(m.Value)).Select(o => o.Id);
            var merged = baseIds.Concat(hits.Where(id => !baseIds.Contains(id))).ToList();

            // lines the box touches come along too, so several can be restyled or deleted at once
            var byId = Doc.Objs.ToDictionary(o => o.Id);
            var connHits = Doc.Conns
                .Where(c => TryResolve(c, byId, out var f, out var t)
                            && m.Value.IntersectsPolyline(ConnectorGeometry.Polyline(c, f, t)))
                .Select(c => c.Id);
            var mergedConns = baseConns.Concat(connHits.Where(id => !baseConns.Contains(id))).ToList();

            Select(merged, mergedConns);
        });
    }

    void StartMoveSelection(BoardObject hit, Pt start, bool shift)
    {
        if (shift)
        {
            if (SelectedIds.Contains(hit.Id)) SelectedIds.Remove(hit.Id);
            else SelectedIds.Add(hit.Id);
            Notify();
            return;
        }

        if (!SelectedIds.Contains(hit.Id))
        {
            SelectedIds.Clear();
            SelectedIds.Add(hit.Id);
            SelectedConnectorIds.Clear();
        }
        Notify();

        if (EditingId == hit.Id) return;
        if (ReadOnly) return;

        var ids = SelectedIds.ToList();
        var origin = Doc.Objs.Where(o => ids.Contains(o.Id)).ToDictionary(o => o.Id, o => (o.X, o.Y));
        var moved = false;
        Drag((w, _) =>
        {
            if (!moved && Math.Abs(w.X - start.X) + Math.Abs(w.Y - start.Y) < 3) return;
            if (!moved)
            {
                moved = true;
                Editor.Snapshot();
            }
            var dx = w.X - start.X;
            var dy = w.Y - start.Y;
            foreach (var o in Doc.Objs.Where(o => ids.Contains(o.Id)))
            {
                o.X = Math.Round(origin[o.Id].X + dx);
                o.Y = Math.Round(origin[o.Id].Y + dy);
            }
            Notify();
        });
    }

    // ---------- resize handles ----------

    public static readonly (string Dir, double Fx, double Fy)[] Handles =
    [
        ("nw", 0, 0), ("n", 0.5, 0), ("ne", 1, 0), ("e", 1, 0.5),
        ("se", 1, 1), ("s", 0.5, 1), ("sw", 0, 1), ("w", 0, 0.5),
    ];

    /// <summary>Which resize handle is under a screen point, if any.</summary>
    public string? HandleAt(Pt screen)
    {
        if (ReadOnly || SingleSelection is not { } o) return null;
        var b = Bounds.Of(o);
        const double grab = 9;
        foreach (var (dir, fx, fy) in Handles)
        {
            var hx = (b.X + fx * b.W) * Doc.Cam.Z + Doc.Cam.X;
            var hy = (b.Y + fy * b.H) * Doc.Cam.Z + Doc.Cam.Y;
            if (Math.Abs(screen.X - hx) <= grab / 2 + 2 && Math.Abs(screen.Y - hy) <= grab / 2 + 2) return dir;
        }
        return null;
    }

    void StartResize(BoardObject o, string dir, Pt start)
    {
        var b = Bounds.Of(o);
        var fixedHeight = o.Kind is ObjKind.Text or ObjKind.Table or ObjKind.Prompt;
        var moved = false;
        IsResizing = true;
        Notify();

        Drag((w, _) =>
        {
            if (!moved)
            {
                moved = true;
                Editor.Snapshot();
            }
            double x = b.X, y = b.Y, width = b.W, height = b.H;
            var dx = w.X - start.X;
            var dy = w.Y - start.Y;
            if (dir.Contains('e')) width = Math.Max(32, b.W + dx);
            if (dir.Contains('s')) height = Math.Max(32, b.H + dy);
            if (dir.Contains('w')) { width = Math.Max(32, b.W - dx); x = b.X + (b.W - width); }
            if (dir.Contains('n')) { height = Math.Max(32, b.H - dy); y = b.Y + (b.H - height); }

            o.X = Math.Round(x);
            o.W = Math.Round(width);
            if (!fixedHeight)
            {
                o.Y = Math.Round(y);
                o.H = Math.Round(height);
            }
            else o.Y = Math.Round(b.Y);
            Notify();
        }, (_, _) =>
        {
            IsResizing = false;
            Notify();
        });
    }

    // ---------- connector hit test ----------

    string? ConnectorHitTest(Pt world)
    {
        var byId = Doc.Objs.ToDictionary(o => o.Id);
        var tolerance = 10 / Doc.Cam.Z;   // a steady ~10 px of slack at any zoom
        foreach (var c in Doc.Conns)
        {
            if (!TryResolve(c, byId, out var from, out var to)) continue;
            var route = ConnectorGeometry.Polyline(c, from, to);
            for (var i = 0; i < route.Count - 1; i++)
                if (DistanceToSegment(world, route[i], route[i + 1]) < tolerance)
                    return c.Id;
        }
        return null;
    }

    /// <summary>The two grab points of the selected connector, in world coordinates.</summary>
    public (Pt A, Pt B)? SelectedConnectorEnds()
    {
        if (SelectedConnector is not { } id) return null;
        if (Doc.Conns.FirstOrDefault(x => x.Id == id) is not { } c) return null;
        var byId = Doc.Objs.ToDictionary(o => o.Id);
        if (!TryResolve(c, byId, out var from, out var to)) return null;
        return ConnectorGeometry.Ends(c, from, to);
    }

    /// <summary>0 = the start handle, 1 = the end handle, null = not on a handle.</summary>
    public int? ConnectorEndAt(Pt screen)
    {
        if (ReadOnly || SelectedConnectorEnds() is not { } ends) return null;
        Pt ToScreen(Pt w) => new(w.X * Doc.Cam.Z + Doc.Cam.X, w.Y * Doc.Cam.Z + Doc.Cam.Y);
        if ((ToScreen(ends.A) - screen).Length <= 8) return 0;
        if ((ToScreen(ends.B) - screen).Length <= 8) return 1;
        return null;
    }

    /// <summary>Drags one end of the selected connector: drop it on an object to attach, or anywhere to leave it free.</summary>
    void StartConnectorEndDrag(int end)
    {
        if (Doc.Conns.FirstOrDefault(x => x.Id == SelectedConnector) is not { } c) return;
        Editor.Snapshot();

        Drag((w, _) =>
        {
            if (end == 0) { c.From = ""; c.FromPt = [w.X, w.Y]; }
            else { c.To = ""; c.ToPt = [w.X, w.Y]; }
            Notify();
        }, (w, _) =>
        {
            var otherId = end == 0 ? c.To : c.From;
            if (Bounds.ObjectAt(Doc.Objs, w) is { } target && target.Id != otherId)
            {
                if (end == 0) { c.From = target.Id; c.FromPt = null; }
                else { c.To = target.Id; c.ToPt = null; }
            }
            Notify();
        });
    }

    /// <summary>False when an end points at an object that is no longer on the board.</summary>
    public static bool TryResolve(Connector c, IReadOnlyDictionary<string, BoardObject> byId,
        out BoardObject? from, out BoardObject? to)
    {
        from = null;
        to = null;
        if (!c.FromIsFree && !byId.TryGetValue(c.From, out from)) return false;
        if (!c.ToIsFree && !byId.TryGetValue(c.To, out to)) return false;
        return true;
    }

    static double DistanceToSegment(Pt p, Pt a, Pt b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lenSq = dx * dx + dy * dy;
        if (lenSq == 0) return (p - a).Length;
        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0, 1);
        return (p - new Pt(a.X + t * dx, a.Y + t * dy)).Length;
    }
}
