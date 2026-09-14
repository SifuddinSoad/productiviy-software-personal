using FocusLock.Core.Board;
using FocusLock.Core.Models;

namespace FocusLock.Core.Export;

/// <summary>A rectangle on a given page, in points.</summary>
public readonly record struct PagedRect(int PageIndex, Rect Rect);

/// <summary>
/// How a text box lays out where it currently sits: the height of its part on its own page, and the
/// parts that run on to the following pages.
/// </summary>
public readonly record struct TextMeasure(double FirstHeight, IReadOnlyList<PagedRect> Rest);

/// <summary>
/// Keeps sections and text boxes from overlapping. Each page is read top to bottom, ordered by the
/// vertical centre of each thing — except the thing just moved, which counts from its top, so even a
/// tall box can be dropped before a short one. Anything that overlaps something before it (and
/// shares some of its width) moves down below it. So dropping a box with its top over the top half of
/// another puts it before that one, over the bottom half after it. What no longer fits goes to the top of the next page, ahead of
/// what is already there, and pages are added when needed. Text that runs on to a page takes the top
/// of it, and that page's own things move below.
///
/// Things never move up and never sideways, so a layout with no overlaps is left exactly as it is.
/// </summary>
public static class PageSettler
{
    const double Tolerance = 0.01;

    /// <param name="measure">Lays a text box out where it sits now; called again whenever it is moved.</param>
    /// <param name="movedId">What the user just moved or added; it goes first when it ties with something.</param>
    public static void Settle(Session session, Func<TextItem, TextMeasure> measure, string? movedId = null)
    {
        if (session.Pages.Count == 0) return;
        var titles = session.PdfTitles;
        var running = new Dictionary<int, List<Rect>>();
        List<object> carry = [];

        for (var p = 0; p < session.Pages.Count; p++)
        {
            var page = session.Pages[p];
            var (_, ph) = PageLayout.SizeOf(page);
            // arrivals start at the top of the text area; what is already here may sit higher, just not in the header
            var (textTop, textBottom) = PageLayout.TextArea(session, page);
            var minTop = session.PdfHeader ? PageLayout.HeaderBand : 0;
            var sectionBottom = session.PdfPageNumbers ? ph - PageLayout.FooterBand : ph;

            var placed = running.Remove(p, out var arrived) ? arrived : [];
            var incoming = carry;
            carry = [];
            List<Rect> leaving = [];

            foreach (var thing in incoming)
            {
                switch (thing)
                {
                    case ExtractItem e: e.PageId = page.Id; e.PageY = textTop; break;
                    case TextItem t: t.PageId = page.Id; t.PageY = textTop; break;
                }
            }

            var existing = session.Extracts.Where(e => e.PageId == page.Id && !incoming.Contains(e)).Cast<object>()
                .Concat(session.TextItems.Where(t => t.PageId == page.Id && !incoming.Contains(t)))
                .Select(thing => (Thing: thing, Key: IdOf(thing) == movedId ? TopOf(thing) : Centre(thing, titles, measure)))
                .OrderBy(x => x.Key)
                .ThenBy(x => IdOf(x.Thing) == movedId ? 0 : 1)
                .Select(x => x.Thing);

            foreach (var thing in incoming.Concat(existing))
            {
                switch (thing)
                {
                    case ExtractItem e:
                    {
                        PageLayout.Clamp(e, page, titles);
                        var h = PageLayout.BoxHeight(e, e.PageW, titles);
                        var y = Below(placed, e.PageX, e.PageW, Math.Max(e.PageY, minTop), h);
                        var box = new Rect(e.PageX, y, e.PageW, h);

                        // once something has gone on, what came after it and shares its width follows
                        if ((y + h > sectionBottom + Tolerance && y > textTop + Tolerance) || Follows(leaving, box))
                        {
                            carry.Add(e);
                            leaving.Add(box);
                            break;
                        }
                        e.PageY = y;
                        placed.Add(box);
                        break;
                    }
                    case TextItem t:
                    {
                        PageLayout.Clamp(t, page);
                        var y = Math.Max(t.PageY, minTop);
                        TextMeasure laid;
                        for (var tries = 0; ; tries++)
                        {
                            t.PageY = y;
                            laid = measure(t);
                            var next = Below(placed, t.PageX, t.PageW, y, laid.FirstHeight);
                            if (next <= y + Tolerance || tries >= 50) break;
                            y = next;
                        }
                        var box = new Rect(t.PageX, y, t.PageW, laid.FirstHeight);

                        if ((y > textBottom - PageLayout.MinTextRoom + Tolerance && y > textTop + Tolerance) || Follows(leaving, box))
                        {
                            carry.Add(t);
                            leaving.Add(box);
                            break;
                        }
                        placed.Add(box);
                        foreach (var part in laid.Rest)
                        {
                            if (!running.TryGetValue(part.PageIndex, out var list)) running[part.PageIndex] = list = [];
                            list.Add(part.Rect);
                        }
                        break;
                    }
                }
            }

            var needed = Math.Max(carry.Count > 0 ? p + 1 : -1, running.Count > 0 ? running.Keys.Max() : -1);
            while (session.Pages.Count <= needed)
                session.Pages.Add(new PdfPage { Id = Ids.New("pg"), Landscape = page.Landscape });
        }
    }

    /// <summary>
    /// Where a box being dragged would land if dropped now: below whatever on the page comes before
    /// it (by the same rule as <see cref="Settle"/>) and shares some of its width.
    /// </summary>
    /// <param name="others">The other boxes on the page it is over, in points.</param>
    public static double LandingTop(IReadOnlyList<Rect> others, Rect moving)
    {
        var before = others.Where(o => o.Y + o.H / 2 < moving.Y).ToList();
        return Below(before, moving.X, moving.W, moving.Y, moving.H);
    }

    /// <summary>
    /// Closes the gaps: everything is put back as high as it can go, in reading order, each keeping
    /// its left edge and width, moving on to the next page when it no longer fits. A page that was
    /// blank before counts as a deliberate break: nothing crosses it, and it stays. Pages emptied by
    /// tidying are removed.
    /// </summary>
    public static void Compact(Session session, Func<TextItem, TextMeasure> measure)
    {
        if (session.Pages.Count == 0) return;
        var titles = session.PdfTitles;

        var things = new Dictionary<string, List<object>>();
        foreach (var page in session.Pages)
            things[page.Id] = session.Extracts.Where(e => e.PageId == page.Id).Cast<object>()
                .Concat(session.TextItems.Where(t => t.PageId == page.Id))
                .OrderBy(x => Centre(x, titles, measure))
                .ToList();
        var blank = session.Pages.Where(p => things[p.Id].Count == 0).Select(p => p.Id).ToHashSet();

        // runs of pages between the deliberate blanks, tidied one run at a time
        List<List<PdfPage>> runs = [[]];
        foreach (var page in session.Pages)
        {
            if (!blank.Contains(page.Id)) runs[^1].Add(page);
            else if (runs[^1].Count > 0) runs.Add([]);
        }

        var used = new HashSet<string>();
        foreach (var run in runs.Where(r => r.Count > 0))
            CompactRun(session, run, run.SelectMany(p => things[p.Id]).ToList(), measure, used);

        session.Pages.RemoveAll(p => !blank.Contains(p.Id) && !used.Contains(p.Id));
    }

    /// <summary>
    /// Packs one run of pages. Only roughly for text that runs on: the settle that always follows
    /// adds any pages it needs and moves what its later parts cover.
    /// </summary>
    static void CompactRun(Session session, List<PdfPage> pages, List<object> order, Func<TextItem, TextMeasure> measure, HashSet<string> used)
    {
        var titles = session.PdfTitles;
        var placed = pages.ToDictionary(p => p.Id, _ => new List<Rect>());
        var p = 0;

        int IndexOf(PdfPage page) => session.Pages.IndexOf(page);

        foreach (var thing in order)
        {
            while (true)
            {
                var page = pages[p];
                var (_, ph) = PageLayout.SizeOf(page);
                var (textTop, textBottom) = PageLayout.TextArea(session, page);
                var sectionBottom = session.PdfPageNumbers ? ph - PageLayout.FooterBand : ph;
                var list = placed[page.Id];
                var fits = false;

                switch (thing)
                {
                    case ExtractItem e:
                    {
                        var h = PageLayout.BoxHeight(e, e.PageW, titles);
                        var y = Below(list, e.PageX, e.PageW, textTop, h);
                        if (y + h <= sectionBottom + Tolerance || y <= textTop + Tolerance)
                        {
                            e.PageId = page.Id;
                            e.PageY = y;
                            list.Add(new Rect(e.PageX, y, e.PageW, h));
                            fits = true;
                        }
                        break;
                    }
                    case TextItem t:
                    {
                        t.PageId = page.Id;
                        var y = textTop;
                        TextMeasure laid;
                        for (var tries = 0; ; tries++)
                        {
                            t.PageY = y;
                            laid = measure(t);
                            var next = Below(list, t.PageX, t.PageW, y, laid.FirstHeight);
                            if (next <= y + Tolerance || tries >= 50) break;
                            y = next;
                        }
                        if (y <= textBottom - PageLayout.MinTextRoom + Tolerance || y <= textTop + Tolerance)
                        {
                            list.Add(new Rect(t.PageX, y, t.PageW, laid.FirstHeight));
                            foreach (var part in laid.Rest)
                            {
                                if (pages.FirstOrDefault(x => IndexOf(x) == part.PageIndex) is not { } covered) continue;
                                placed[covered.Id].Add(part.Rect);
                                used.Add(covered.Id);
                            }
                            fits = true;
                        }
                        break;
                    }
                }

                if (fits)
                {
                    used.Add(page.Id);
                    break;
                }
                if (p == pages.Count - 1)
                {
                    // nowhere left in this run: a new page after its last one
                    var added = new PdfPage { Id = Ids.New("pg"), Landscape = page.Landscape };
                    session.Pages.Insert(IndexOf(page) + 1, added);
                    pages.Add(added);
                    placed[added.Id] = [];
                }
                p++;
            }
        }
    }

    static string IdOf(object thing) => thing switch
    {
        ExtractItem e => e.Id,
        TextItem t => t.Id,
        _ => "",
    };

    static double TopOf(object thing) => thing switch
    {
        ExtractItem e => e.PageY,
        TextItem t => t.PageY,
        _ => 0,
    };

    static double Centre(object thing, bool titles, Func<TextItem, TextMeasure> measure) => thing switch
    {
        ExtractItem e => e.PageY + PageLayout.BoxHeight(e, e.PageW, titles) / 2,
        TextItem t => t.PageY + measure(t).FirstHeight / 2,
        _ => 0,
    };

    static bool SharesWidth(Rect r, double x, double w) => x < r.Right - Tolerance && x + w > r.X + Tolerance;

    /// <summary>The first top at or below <paramref name="y"/> where a box of this width and height overlaps nothing placed.</summary>
    static double Below(List<Rect> placed, double x, double w, double y, double h)
    {
        for (var moved = true; moved;)
        {
            moved = false;
            foreach (var r in placed)
            {
                if (!SharesWidth(r, x, w) || y >= r.Bottom - Tolerance || y + h <= r.Y + Tolerance) continue;
                y = r.Bottom + PageLayout.Gap;
                moved = true;
            }
        }
        return y;
    }

    static bool Follows(List<Rect> leaving, Rect box) =>
        leaving.Any(r => SharesWidth(r, box.X, box.W) && box.Y >= r.Y - Tolerance);
}
