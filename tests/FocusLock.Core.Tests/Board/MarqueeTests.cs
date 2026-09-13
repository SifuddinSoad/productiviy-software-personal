using FocusLock.Core.Board;
using FocusLock.Core.Models;

namespace FocusLock.Core.Tests.Board;

public class MarqueeTests
{
    static BoardObject Shape(string id, double x, double y) =>
        new() { Id = id, Kind = ObjKind.Shape, Shape = "rect", X = x, Y = y, W = 100, H = 60 };

    [Fact]
    public void A_box_over_the_middle_of_a_line_catches_it()
    {
        var c = new Connector { Id = "c1", From = "a", To = "b", Style = "straight" };
        var route = ConnectorGeometry.Polyline(c, Shape("a", 0, 0), Shape("b", 400, 0));

        // a small box straddling the line, touching neither shape
        Assert.True(new Rect(230, 10, 40, 40).IntersectsPolyline(route));
    }

    [Fact]
    public void A_box_away_from_the_line_does_not()
    {
        var c = new Connector { Id = "c1", From = "a", To = "b", Style = "straight" };
        var route = ConnectorGeometry.Polyline(c, Shape("a", 0, 0), Shape("b", 400, 0));

        Assert.False(new Rect(200, 300, 60, 60).IntersectsPolyline(route));
    }

    [Fact]
    public void An_elbow_is_caught_along_its_corner_run()
    {
        var c = new Connector { Id = "c1", From = "a", To = "b", Style = "elbow" };
        var route = ConnectorGeometry.Polyline(c, Shape("a", 0, 0), Shape("b", 400, 300));

        // the vertical middle run sits at x = midpoint of the two attach points
        var mid = route[1];
        Assert.True(new Rect(mid.X - 10, mid.Y + 40, 20, 40).IntersectsPolyline(route));
    }

    [Fact]
    public void A_free_line_is_caught_too()
    {
        var c = new Connector { Id = "c1", FromPt = [0, 0], ToPt = [200, 200], Style = "straight" };
        var route = ConnectorGeometry.Polyline(c, null, null);

        Assert.True(new Rect(90, 90, 20, 20).IntersectsPolyline(route));
        Assert.False(new Rect(150, 20, 20, 20).IntersectsPolyline(route));
    }

    [Fact]
    public void A_box_that_swallows_the_whole_line_catches_it()
    {
        var c = new Connector { Id = "c1", FromPt = [50, 50], ToPt = [80, 90], Style = "curve" };
        var route = ConnectorGeometry.Polyline(c, null, null);

        Assert.True(new Rect(0, 0, 300, 300).IntersectsPolyline(route));
    }
}
