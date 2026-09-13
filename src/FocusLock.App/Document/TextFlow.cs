using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FocusLock.Core.Export;
using FocusLock.Core.Models;

namespace FocusLock.App.Document;

/// <summary>
/// One page's worth of a text box. The first sits where the box was put; each one after it sits at
/// the same left edge, at the top of the following page's text area.
/// </summary>
/// <param name="PageIndex">The page it belongs on; can be past the last page until pages are added.</param>
/// <param name="X">Left edge on the page, in points.</param>
/// <param name="Y">Top edge on the page, in points.</param>
/// <param name="W">Width in points.</param>
/// <param name="H">How much of the page it takes, in points: all the room it had, or for the last one just what its text needs.</param>
/// <param name="Page">The laid-out text, in WPF units, with <paramref name="ShiftDip"/> of empty space above what belongs here.</param>
public sealed record TextFragment(int PageIndex, double X, double Y, double W, double H, DocumentPage Page, double ShiftDip);

/// <summary>
/// Lays a text box out down the pages. WPF's own paginator breaks the text — lines, list numbers and
/// table rows carry on correctly — but it only knows one page height. The first page usually has less
/// room (the box starts part way down), so the text starts with an invisible spacer that uses up the
/// difference; that page is then drawn shifted up by the spacer.
/// </summary>
public static class TextFlow
{
    /// <summary>The height a box with no words still shows, so it can be seen and clicked.</summary>
    public const double EmptyHeightPt = 18;

    public static string PaperOf(Session session) => session.PdfLight ? "#ffffff" : "#121315";

    public static List<TextFragment> Layout(Session session, TextItem item)
    {
        var pages = session.Pages;
        var index = pages.FindIndex(p => p.Id == item.PageId);
        if (index < 0) return [];

        var first = pages[index];
        var (_, firstBottom) = PageLayout.TextArea(session, first);
        var firstRoom = Math.Max(PageLayout.MinTextRoom, firstBottom - item.PageY);

        // pages added for running text are turned like the box's own page, so that is the size to lay out for
        var following = index + 1 < pages.Count ? pages[index + 1] : first;
        var (followTop, followBottom) = PageLayout.TextArea(session, following);
        var height = Math.Max(firstRoom, followBottom - followTop);
        var spacer = height - firstRoom;

        var width = DocLook.Dip(item.PageW);
        var flow = DocumentMapper.Build(item.Blocks, PaperOf(session), width, forPrint: true);
        flow.PageHeight = DocLook.Dip(height);
        if (spacer > 0.01) flow.Blocks.InsertBefore(flow.Blocks.FirstBlock, new BlockUIContainer(new Border { Height = DocLook.Dip(spacer) }));

        var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        paginator.PageSize = new Size(width, DocLook.Dip(height));
        paginator.ComputePageCount();

        var fragments = new List<TextFragment>();
        for (var i = 0; i < paginator.PageCount; i++)
        {
            var page = paginator.GetPage(i);
            var sheet = index + i < pages.Count ? pages[index + i] : first;
            var (top, bottom) = PageLayout.TextArea(session, sheet);
            var y = i == 0 ? item.PageY : top;
            var room = i == 0 ? firstRoom : bottom - top;
            var shift = i == 0 ? DocLook.Dip(spacer) : 0;

            var h = room;
            if (i == paginator.PageCount - 1)
            {
                var bounds = VisualTreeHelper.GetDescendantBounds(page.Visual);
                var used = bounds.IsEmpty ? 0 : (bounds.Bottom - shift) / DocLook.DipPerPoint;
                h = Math.Clamp(used, EmptyHeightPt, room);
            }
            fragments.Add(new TextFragment(index + i, item.PageX, y, item.PageW, h, page, shift));
        }
        return fragments;
    }
}
