using System.Text.Json;
using FocusLock.Core.Models;

namespace FocusLock.Core.Export;

/// <summary>
/// Every change to the PDF layout, plus undo and redo. Like <see cref="Board.BoardEditor"/>, history
/// is a list of snapshots. Section objects are updated in place on undo, so anything holding one
/// keeps holding the live item.
/// </summary>
public sealed class PageDeck(Session session)
{
    const int MaxHistory = 40;

    readonly List<string> _undo = [];
    readonly List<string> _redo = [];

    public Session Session { get; } = session;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    sealed record Snap(List<PdfPage> Pages, List<ExtractItem> Extracts, bool Titles, bool Light);

    string Serialize() =>
        JsonSerializer.Serialize(new Snap(Session.Pages, Session.Extracts, Session.PdfTitles, Session.PdfLight));

    void Restore(string json)
    {
        var snap = JsonSerializer.Deserialize<Snap>(json)!;
        var live = Session.Extracts.ToDictionary(e => e.Id);

        Session.Pages.Clear();
        Session.Pages.AddRange(snap.Pages);
        Session.PdfTitles = snap.Titles;
        Session.PdfLight = snap.Light;

        Session.Extracts.Clear();
        foreach (var saved in snap.Extracts)
        {
            if (!live.TryGetValue(saved.Id, out var item))
            {
                Session.Extracts.Add(saved);
                continue;
            }
            item.PlanId = saved.PlanId;
            item.Name = saved.Name;
            (item.X, item.Y, item.W, item.H) = (saved.X, saved.Y, saved.W, saved.H);
            (item.PageId, item.PageX, item.PageY, item.PageW) = (saved.PageId, saved.PageX, saved.PageY, saved.PageW);
            Session.Extracts.Add(item);
        }
    }

    /// <summary>Call immediately before a change that should be undoable. A drag calls it once, at the start.</summary>
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

    public PdfPage? PageById(string id) => Session.Pages.FirstOrDefault(p => p.Id == id);

    public ExtractItem? ItemById(string id) => Session.Extracts.FirstOrDefault(e => e.Id == id);

    public IEnumerable<ExtractItem> ItemsOn(string pageId) => Session.Extracts.Where(e => e.PageId == pageId);

    /// <summary>Moves and sizes a section, onto any page, and keeps it on that page. Takes no snapshot.</summary>
    public void Place(string itemId, string pageId, double x, double y, double width)
    {
        if (ItemById(itemId) is not { } item || PageById(pageId) is not { } page) return;
        item.PageId = pageId;
        item.PageX = x;
        item.PageY = y;
        item.PageW = width;
        PageLayout.Clamp(item, page, Session.PdfTitles);
    }

    public PdfPage AddPage()
    {
        Snapshot();
        var page = new PdfPage { Id = Ids.New("pg") };
        Session.Pages.Add(page);
        return page;
    }

    /// <summary>Its sections go to the page before it, or after it for the first page. The last page stays.</summary>
    public bool DeletePage(string pageId)
    {
        var index = Session.Pages.FindIndex(p => p.Id == pageId);
        if (index < 0 || Session.Pages.Count == 1) return false;

        Snapshot();
        var heir = Session.Pages[index == 0 ? 1 : index - 1];
        Session.Pages.RemoveAt(index);
        foreach (var item in ItemsOn(pageId).ToList())
        {
            item.PageId = heir.Id;
            PageLayout.Clamp(item, heir, Session.PdfTitles);
        }
        return true;
    }

    public void SetLandscape(string pageId, bool landscape)
    {
        if (PageById(pageId) is not { } page || page.Landscape == landscape) return;
        Snapshot();
        page.Landscape = landscape;
        foreach (var item in ItemsOn(pageId)) PageLayout.Clamp(item, page, Session.PdfTitles);
    }

    public void SetTitles(bool titles)
    {
        if (Session.PdfTitles == titles) return;
        Snapshot();
        Session.PdfTitles = titles;
        foreach (var page in Session.Pages)
            foreach (var item in ItemsOn(page.Id)) PageLayout.Clamp(item, page, titles);
    }

    public void SetLight(bool light)
    {
        if (Session.PdfLight == light) return;
        Snapshot();
        Session.PdfLight = light;
    }

    public void Remove(string itemId)
    {
        if (ItemById(itemId) is null) return;
        Snapshot();
        Session.Extracts.RemoveAll(e => e.Id == itemId);
    }
}
