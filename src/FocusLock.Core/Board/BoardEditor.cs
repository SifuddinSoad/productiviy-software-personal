using System.Text.Json;
using FocusLock.Core.Models;

namespace FocusLock.Core.Board;

/// <summary>
/// All document mutations plus undo/redo. Like the design, undo stores a JSON snapshot of
/// objects, connectors and strokes (the camera is not part of history) and keeps 40 steps.
/// </summary>
public sealed class BoardEditor(BoardDoc doc)
{
    const int MaxHistory = 40;

    static readonly JsonSerializerOptions SnapshotOptions = new();

    readonly List<string> _undo = [];
    readonly List<string> _redo = [];

    public BoardDoc Doc { get; } = doc;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    sealed record Snap(List<BoardObject> Objs, List<Connector> Conns, List<Stroke> Strokes);

    string Serialize() => JsonSerializer.Serialize(new Snap(Doc.Objs, Doc.Conns, Doc.Strokes), SnapshotOptions);

    void Restore(string json)
    {
        var s = JsonSerializer.Deserialize<Snap>(json, SnapshotOptions)!;
        Doc.Objs.Clear();
        Doc.Objs.AddRange(s.Objs);
        Doc.Conns.Clear();
        Doc.Conns.AddRange(s.Conns);
        Doc.Strokes.Clear();
        Doc.Strokes.AddRange(s.Strokes);
    }

    /// <summary>Call immediately before a change that should be undoable.</summary>
    public void Snapshot()
    {
        _undo.Add(Serialize());
        if (_undo.Count > MaxHistory) _undo.RemoveAt(0);
        _redo.Clear();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var current = Serialize();
        Restore(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(current);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var current = Serialize();
        Restore(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(current);
    }

    public BoardObject? ById(string id) => Doc.Objs.FirstOrDefault(o => o.Id == id);

    public void Add(BoardObject o)
    {
        Snapshot();
        Doc.Objs.Add(o);
    }

    public void AddConnector(Connector c)
    {
        Snapshot();
        Doc.Conns.Add(c);
    }

    /// <summary>Removes objects and any connector attached to them.</summary>
    public void Delete(IReadOnlyCollection<string> ids) => Delete(ids, []);

    public void DeleteConnector(string id) => Delete([], [id]);

    /// <summary>Removes objects and connectors together, as one undo step.</summary>
    public void Delete(IReadOnlyCollection<string> objectIds, IReadOnlyCollection<string> connectorIds)
    {
        if (objectIds.Count == 0 && connectorIds.Count == 0) return;
        Snapshot();
        if (objectIds.Count > 0)
        {
            Doc.Objs.RemoveAll(o => objectIds.Contains(o.Id));
            Doc.Conns.RemoveAll(c => objectIds.Contains(c.From) || objectIds.Contains(c.To));
        }
        if (connectorIds.Count > 0) Doc.Conns.RemoveAll(c => connectorIds.Contains(c.Id));
    }

    /// <summary>Copies offset by 26,26 like the design; returns the new ids.</summary>
    public List<string> Duplicate(IReadOnlyCollection<string> ids, Func<string> newId)
    {
        if (ids.Count == 0) return [];
        Snapshot();
        var copies = Doc.Objs.Where(o => ids.Contains(o.Id)).Select(o =>
        {
            var c = o.Clone();
            c.Id = newId();
            c.X += 26;
            c.Y += 26;
            return c;
        }).ToList();
        Doc.Objs.AddRange(copies);
        return copies.Select(c => c.Id).ToList();
    }

    public void SetFill(IReadOnlyCollection<string> ids, string fill)
    {
        Snapshot();
        foreach (var o in Doc.Objs.Where(o => ids.Contains(o.Id))) o.Fill = fill;
    }

    /// <summary>Null restores the automatic colour that follows the fill.</summary>
    public void SetTextColor(IReadOnlyCollection<string> ids, string? color)
    {
        Snapshot();
        foreach (var o in Doc.Objs.Where(o => ids.Contains(o.Id))) o.TextColor = color;
    }

    public void AddVotes(IReadOnlyCollection<string> ids, int delta)
    {
        Snapshot();
        foreach (var o in Doc.Objs.Where(o => ids.Contains(o.Id)))
            o.Votes = Math.Max(0, o.Votes + delta);
    }

    public void SetText(string id, string text)
    {
        if (ById(id) is not { } o || o.Text == text) return;
        Snapshot();
        o.Text = text;
    }

    public void SetCell(string id, int index, string text)
    {
        if (ById(id) is not { Cells: { } cells } o || index >= cells.Count || cells[index] == text) return;
        Snapshot();
        cells[index] = text;
    }

    public void AlignLeft(IReadOnlyCollection<string> ids) => Align(ids, sel =>
    {
        var x = sel.Min(o => o.X);
        foreach (var o in sel) o.X = x;
    });

    public void AlignTop(IReadOnlyCollection<string> ids) => Align(ids, sel =>
    {
        var y = sel.Min(o => o.Y);
        foreach (var o in sel) o.Y = y;
    });

    public void AlignCenterHorizontally(IReadOnlyCollection<string> ids) => Align(ids, sel =>
    {
        var c = sel.Average(o => o.X + Bounds.Of(o).W / 2);
        foreach (var o in sel) o.X = Math.Round(c - Bounds.Of(o).W / 2);
    });

    /// <summary>Stacks the selection top to bottom with a 24 px gap, like the design.</summary>
    public void DistributeVertically(IReadOnlyCollection<string> ids) => Align(ids, sel =>
    {
        var sorted = sel.OrderBy(o => o.Y).ToList();
        var y = sorted[0].Y;
        foreach (var o in sorted)
        {
            o.Y = Math.Round(y);
            y += Bounds.Of(o).H + 24;
        }
    });

    void Align(IReadOnlyCollection<string> ids, Action<List<BoardObject>> apply)
    {
        var sel = Doc.Objs.Where(o => ids.Contains(o.Id)).ToList();
        if (sel.Count == 0) return;
        Snapshot();
        apply(sel);
    }

    public void ApplyErase(Eraser.Result result)
    {
        if (!result.Changed) return;
        Snapshot();
        Doc.Strokes.Clear();
        Doc.Strokes.AddRange(result.Strokes);
        if (result.RemovedObject is { } target)
        {
            Doc.Objs.RemoveAll(o => o.Id == target.Id);
            Doc.Conns.RemoveAll(c => c.From == target.Id || c.To == target.Id);
        }
    }
}
