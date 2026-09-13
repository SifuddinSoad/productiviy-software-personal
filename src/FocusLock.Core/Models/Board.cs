namespace FocusLock.Core.Models;

/// <summary>One canvas: the same shape as the design's { objs, conns, strokes, cam } doc.</summary>
public sealed class BoardDoc
{
    public List<BoardObject> Objs { get; set; } = [];
    public List<Connector> Conns { get; set; } = [];
    public List<Stroke> Strokes { get; set; } = [];
    public Camera Cam { get; set; } = new();
}

public static class ObjKind
{
    public const string Frame = "frame";
    public const string Text = "text";
    public const string Sticky = "sticky";
    public const string Shape = "shape";
    public const string Table = "table";
    public const string Prompt = "prompt";
}

public sealed class BoardObject
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = ObjKind.Sticky;
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public string Text { get; set; } = "";
    public string? Fill { get; set; }
    public int Votes { get; set; }
    public double Rot { get; set; }

    // shape
    public string? Shape { get; set; }
    public List<double[]>? Pts { get; set; }

    // text
    public double? Size { get; set; }
    public int? Weight { get; set; }

    // table
    public int? Cols { get; set; }
    public int? Rows { get; set; }
    public List<string>? Cells { get; set; }

    // prompt: creation label ("now", "2m" ...)
    public string? T { get; set; }

    public BoardObject Clone()
    {
        var c = (BoardObject)MemberwiseClone();
        c.Pts = Pts?.Select(p => (double[])p.Clone()).ToList();
        c.Cells = Cells is null ? null : [.. Cells];
        return c;
    }
}

public sealed class Connector
{
    public string Id { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Style { get; set; } = "curve";   // curve | elbow | straight
    public string Arrows { get; set; } = "end";    // end | both | none
    public bool Dash { get; set; }
}

public sealed class Stroke
{
    public string Id { get; set; } = "";
    public List<double[]> Pts { get; set; } = [];
    public string Color { get; set; } = "#e9e9e7";
    public double W { get; set; } = 2.4;
}

public sealed class Camera
{
    public double X { get; set; } = 120;
    public double Y { get; set; } = 60;
    public double Z { get; set; } = 1;
}
