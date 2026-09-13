using FocusLock.Core.Export;

namespace FocusLock.Core.Tests.Export;

public class PageLayoutTests
{
    const double Margin = PageLayout.Margin;

    [Fact]
    public void A_small_region_gets_a_small_page_at_full_size()
    {
        // 300 x 200 canvas units = 225 x 150 pt
        var page = PageLayout.PageFor(300, 200);

        Assert.Equal(1, page.Scale);
        Assert.Equal(225 + 2 * Margin, page.Width, 1);
        Assert.Equal(150 + 2 * Margin, page.Height, 1);
    }

    [Fact]
    public void A_region_larger_than_A4_is_scaled_down_to_its_ceiling()
    {
        // 2:3 is taller than A4, so height is what runs out; the page keeps the region's shape
        var page = PageLayout.PageFor(4000, 6000);

        Assert.True(page.Scale < 1);
        Assert.Equal(PageLayout.A4Long, page.Height, 1);
        Assert.True(page.Width < PageLayout.A4Short);
    }

    [Fact]
    public void A_region_shaped_like_the_printable_area_fills_the_whole_sheet()
    {
        // the margins are not proportional, so it is the area inside them that has to match
        var units = 1 / PageLayout.PointsPerUnit;
        var width = (PageLayout.A4Short - 2 * Margin) * units;
        var height = (PageLayout.A4Long - 2 * Margin) * units;

        var page = PageLayout.PageFor(width, height);

        Assert.Equal(PageLayout.A4Short, page.Width, 1);
        Assert.Equal(PageLayout.A4Long, page.Height, 1);
    }

    [Fact]
    public void A_wide_region_gets_a_landscape_page()
    {
        var page = PageLayout.PageFor(3000, 900);

        Assert.True(page.Width > page.Height);
        Assert.True(page.Width <= PageLayout.A4Long + 0.5);
    }

    [Fact]
    public void A_tall_region_gets_a_portrait_page()
    {
        var page = PageLayout.PageFor(900, 3000);

        Assert.True(page.Height > page.Width);
        Assert.True(page.Height <= PageLayout.A4Long + 0.5);
    }

    [Fact]
    public void A_tiny_region_still_gets_a_usable_card()
    {
        var page = PageLayout.PageFor(10, 10);

        Assert.Equal(PageLayout.MinSide, page.Width);
        Assert.Equal(PageLayout.MinSide, page.Height);
    }

    [Fact]
    public void A_caption_adds_height_but_not_width()
    {
        var plain = PageLayout.PageFor(600, 400);
        var titled = PageLayout.PageFor(600, 400, caption: true);

        Assert.Equal(plain.Width, titled.Width, 1);
        Assert.Equal(plain.Height + PageLayout.CaptionHeight, titled.Height, 1);
    }

    [Fact]
    public void An_empty_region_does_not_produce_a_broken_page()
    {
        var page = PageLayout.PageFor(0, 0);

        Assert.Equal(PageLayout.MinSide, page.Width);
        Assert.Equal(PageLayout.MinSide, page.Height);
        Assert.True(page.Scale > 0);
    }

    [Fact]
    public void The_drawn_region_always_fits_inside_its_page()
    {
        foreach (var (w, h) in new[] { (300.0, 200.0), (4000.0, 6000.0), (3000.0, 900.0), (50.0, 2000.0) })
        {
            var page = PageLayout.PageFor(w, h, caption: true);
            var drawnWidth = w * PageLayout.PointsPerUnit * page.Scale;
            var drawnHeight = h * PageLayout.PointsPerUnit * page.Scale;

            Assert.True(drawnWidth <= page.Width - 2 * Margin + 0.5, $"{w}x{h} too wide");
            Assert.True(drawnHeight <= page.Height - 2 * Margin - PageLayout.CaptionHeight + 0.5, $"{w}x{h} too tall");
        }
    }
}
