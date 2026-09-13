using FocusLock.Core.Board;
using FocusLock.Core.Export;

namespace FocusLock.Core.Tests.Export;

public class SnappingTests
{
    const double PageW = 595, PageH = 842, M = PageLayout.Margin, T = 6;

    [Fact]
    public void A_box_near_the_left_margin_lands_on_it_with_a_guide()
    {
        var r = Snapping.Move(new Rect(22, 300, 200, 100), PageW, PageH, [], T);

        Assert.Equal(M, r.Box.X);
        Assert.Equal(300, r.Box.Y);
        Assert.Contains(new Guide(Vertical: true, At: M), r.Guides);
    }

    [Fact]
    public void A_box_whose_centre_is_near_the_page_centre_is_centred()
    {
        var r = Snapping.Move(new Rect(PageW / 2 - 100 + 4, 300, 200, 100), PageW, PageH, [], T);

        Assert.Equal(PageW / 2 - 100, r.Box.X, 6);
        Assert.Contains(new Guide(true, PageW / 2), r.Guides);
    }

    [Fact]
    public void Nothing_snaps_beyond_the_threshold()
    {
        var box = new Rect(120, 300, 200, 100);
        var r = Snapping.Move(box, PageW, PageH, [], T);

        Assert.Equal(box, r.Box);
        Assert.Empty(r.Guides);
    }

    [Fact]
    public void A_box_lines_up_under_another_one_on_both_axes()
    {
        var other = new Rect(60, 100, 250, 120);
        // left edge 3 pt off the other's left, top 4 pt below the other's bottom
        var r = Snapping.Move(new Rect(63, 224, 180, 90), PageW, PageH, [other], T);

        Assert.Equal(new Rect(60, 220, 180, 90), r.Box);
        Assert.Contains(new Guide(true, 60), r.Guides);
        Assert.Contains(new Guide(false, 220), r.Guides);
    }

    [Fact]
    public void The_closest_line_wins()
    {
        var a = new Rect(0, 500, 100, 50);   // right edge at 100
        var b = new Rect(0, 600, 104, 50);   // right edge at 104
        var r = Snapping.Move(new Rect(103, 300, 50, 50), PageW, PageH, [a, b], T);

        Assert.Equal(104, r.Box.X);
    }

    [Fact]
    public void Resizing_the_bottom_right_corner_keeps_the_shape_and_snaps_to_the_right_margin()
    {
        // picture is twice as wide as tall; no caption
        var r = Snapping.Resize(anchor: new Pt(100, 100), pointer: new Pt(PageW - M - 3, 250), corner: "se",
            aspect: 0.5, captionHeight: 0, PageW, PageH, [], T, minWidth: 40);

        Assert.Equal(100, r.Box.X);
        Assert.Equal(100, r.Box.Y);
        Assert.Equal(PageW - M, r.Box.Right, 6);
        Assert.Equal(r.Box.W * 0.5, r.Box.H, 6);
        Assert.Contains(new Guide(true, PageW - M), r.Guides);
    }

    [Fact]
    public void Resizing_follows_whichever_axis_the_pointer_has_gone_further_along()
    {
        var r = Snapping.Resize(new Pt(100, 100), new Pt(200, 400), "se", aspect: 0.5, captionHeight: 22,
            PageW, PageH, [], threshold: 0, minWidth: 40);

        // 100 wide by the x distance, (300 - 22) / 0.5 = 556 wide by the y distance
        Assert.Equal(556, r.Box.W, 6);
        Assert.Equal(556 * 0.5 + 22, r.Box.H, 6);
    }

    [Fact]
    public void Resizing_the_top_left_corner_keeps_the_bottom_right_fixed()
    {
        var r = Snapping.Resize(new Pt(400, 400), new Pt(200, 380), "nw", aspect: 0.5, captionHeight: 0,
            PageW, PageH, [], threshold: 0, minWidth: 40);

        Assert.Equal(400, r.Box.Right, 6);
        Assert.Equal(400, r.Box.Bottom, 6);
        Assert.Equal(200, r.Box.W, 6);
    }

    [Fact]
    public void Dragging_a_text_box_right_edge_snaps_it_to_the_margin_and_keeps_the_left_edge()
    {
        var r = Snapping.ResizeWidth(left: 50, right: 300, pointerX: PageW - M - 4, dragRight: true, PageW, [], T, minWidth: 60);

        Assert.Equal(50, r.X);
        Assert.Equal(PageW - M - 50, r.W, 6);
        Assert.Contains(new Guide(true, PageW - M), r.Guides);
    }

    [Fact]
    public void Dragging_a_text_box_left_edge_keeps_the_right_edge_and_the_minimum_width()
    {
        var r = Snapping.ResizeWidth(left: 50, right: 300, pointerX: 290, dragRight: false, PageW, [], threshold: 0, minWidth: 60);

        Assert.Equal(300, r.X + r.W, 6);
        Assert.Equal(60, r.W, 6);
        Assert.Empty(r.Guides);
    }

    [Fact]
    public void Resizing_never_goes_below_the_minimum_width()
    {
        var r = Snapping.Resize(new Pt(100, 100), new Pt(101, 101), "se", 0.5, 0, PageW, PageH, [], 0, minWidth: 40);

        Assert.Equal(40, r.Box.W, 6);
    }
}
