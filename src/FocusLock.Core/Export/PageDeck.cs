using System.Text.Json;
using FocusLock.Core.Document;
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

    sealed record Snap(
        List<PdfPage> Pages, List<ExtractItem> Extracts, List<TextItem> Texts,
        bool Titles, bool Light, bool Header, string HeaderText, bool PageNumbers);

    string Serialize() => JsonSerializer.Serialize(new Snap(
        Session.Pages, Session.Extracts, Session.TextItems,
        Session.PdfTitles, Session.PdfLight, Session.PdfHeader, Session.PdfHeaderText, Session.PdfPageNumbers));

    void Restore(string json)
    {
        var snap = JsonSerializer.Deserialize<Snap>(json)!;
        var live = Session.Extracts.ToDictionary(e => e.Id);

        Session.Pages.Clear();
        Session.Pages.AddRange(snap.Pages);
        Session.PdfTitles = snap.Titles;
        Session.PdfLight = snap.Light;
        Session.PdfHeader = snap.Header;
        Session.PdfHeaderText = snap.HeaderText;
        Session.PdfPageNumbers = snap.PageNumbers;

        // text boxes come back as new objects; their blocks are new lists, which is what tells views the text changed
        Session.TextItems.Clear();
        Session.TextItems.AddRange(snap.Texts);

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

    /// <summary>Forgets the last snapshot, for a step that turned out to change nothing (a text box opened and closed).</summary>
    public void DiscardSnapshot()
    {
        if (_undo.Count > 0) _undo.RemoveAt(_undo.Count - 1);
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
        foreach (var text in TextsOn(pageId).ToList())
        {
            text.PageId = heir.Id;
            PageLayout.Clamp(text, heir);
        }
        return true;
    }

    public void SetLandscape(string pageId, bool landscape)
    {
        if (PageById(pageId) is not { } page || page.Landscape == landscape) return;
        Snapshot();
        page.Landscape = landscape;
        foreach (var item in ItemsOn(pageId)) PageLayout.Clamp(item, page, Session.PdfTitles);
        foreach (var text in TextsOn(pageId)) PageLayout.Clamp(text, page);
    }

    // ---------------------------------------------------------------- text boxes

    public TextItem? TextById(string id) => Session.TextItems.FirstOrDefault(t => t.Id == id);

    public IEnumerable<TextItem> TextsOn(string pageId) => Session.TextItems.Where(t => t.PageId == pageId);

    public TextItem AddText(string pageId, double x, double y, double width, List<DocBlock> blocks)
    {
        Snapshot();
        var text = new TextItem { Id = Ids.New("t"), PageId = pageId, PageX = x, PageY = y, PageW = width, Blocks = blocks };
        if (PageById(pageId) is { } page) PageLayout.Clamp(text, page);
        Session.TextItems.Add(text);
        return text;
    }

    /// <summary>Moves and sizes a text box, onto any page. Takes no snapshot.</summary>
    public void PlaceText(string textId, string pageId, double x, double y, double width)
    {
        if (TextById(textId) is not { } text || PageById(pageId) is not { } page) return;
        text.PageId = pageId;
        text.PageX = x;
        text.PageY = y;
        text.PageW = width;
        PageLayout.Clamp(text, page);
    }

    /// <summary>New words for a text box. Takes no snapshot: the edit took one when it began.</summary>
    public void SetText(string textId, List<DocBlock> blocks)
    {
        if (TextById(textId) is { } text) text.Blocks = blocks;
    }

    public void RemoveText(string textId)
    {
        if (TextById(textId) is null) return;
        Snapshot();
        Session.TextItems.RemoveAll(t => t.Id == textId);
    }

    /// <summary>
    /// Makes sure <paramref name="count"/> pages follow the given one, adding pages turned the same
    /// way, so text running over has somewhere to go. Takes no snapshot: it belongs to the edit or
    /// move that made the text run over.
    /// </summary>
    public void EnsurePagesAfter(string pageId, int count)
    {
        var index = Session.Pages.FindIndex(p => p.Id == pageId);
        if (index < 0) return;
        var landscape = Session.Pages[index].Landscape;
        while (Session.Pages.Count - 1 - index < count)
            Session.Pages.Add(new PdfPage { Id = Ids.New("pg"), Landscape = landscape });
    }

    // ---------------------------------------------------------------- header and page numbers

    /// <summary>Turning it on moves anything sitting in the header's band down below it.</summary>
    public void SetHeader(bool on)
    {
        if (Session.PdfHeader == on) return;
        Snapshot();
        Session.PdfHeader = on;
        if (!on) return;

        foreach (var page in Session.Pages)
        {
            foreach (var item in ItemsOn(page.Id).Where(e => e.PageY < PageLayout.HeaderBand))
            {
                item.PageY = PageLayout.HeaderBand;
                PageLayout.Clamp(item, page, Session.PdfTitles);
            }
            foreach (var text in TextsOn(page.Id).Where(t => t.PageY < PageLayout.HeaderBand))
            {
                text.PageY = PageLayout.HeaderBand;
                PageLayout.Clamp(text, page);
            }
        }
    }

    public void SetHeaderText(string text)
    {
        text = text.Trim();
        if (Session.PdfHeaderText == text) return;
        Snapshot();
        Session.PdfHeaderText = text;
    }

    public void SetPageNumbers(bool on)
    {
        if (Session.PdfPageNumbers == on) return;
        Snapshot();
        Session.PdfPageNumbers = on;
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

    /// <summary>
    /// A copy of a section or text box straight below the original, next to it in the list. The
    /// caller settles afterwards, so what was below makes room.
    /// </summary>
    /// <param name="textHeight">A text box's height on its page; only the app can lay text out.</param>
    /// <returns>The copy's id, or null when there is nothing with that id.</returns>
    public string? Duplicate(string id, double textHeight)
    {
        if (ItemById(id) is { } item)
        {
            Snapshot();
            var copy = new ExtractItem
            {
                Id = Ids.New("x"), PlanId = item.PlanId, Name = item.Name.Length == 0 ? "copy" : $"{item.Name} copy",
                X = item.X, Y = item.Y, W = item.W, H = item.H,
                PageId = item.PageId, PageX = item.PageX, PageW = item.PageW,
                PageY = PageLayout.BoxOf(item, Session.PdfTitles).Bottom + PageLayout.Gap,
            };
            Session.Extracts.Insert(Session.Extracts.IndexOf(item) + 1, copy);
            return copy.Id;
        }

        if (TextById(id) is { } text)
        {
            Snapshot();
            var copy = new TextItem
            {
                Id = Ids.New("t"), PageId = text.PageId, PageX = text.PageX, PageW = text.PageW,
                PageY = text.PageY + textHeight + PageLayout.Gap,
                Blocks = JsonSerializer.Deserialize<List<DocBlock>>(JsonSerializer.Serialize(text.Blocks))!,
            };
            Session.TextItems.Insert(Session.TextItems.IndexOf(text) + 1, copy);
            return copy.Id;
        }
        return null;
    }

    public void Remove(string itemId)
    {
        if (ItemById(itemId) is null) return;
        Snapshot();
        Session.Extracts.RemoveAll(e => e.Id == itemId);
    }
}
