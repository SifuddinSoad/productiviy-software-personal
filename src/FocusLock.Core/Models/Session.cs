using FocusLock.Core.Document;

namespace FocusLock.Core.Models;

public enum EndReason
{
    Completed,
    Emergency,

    /// <summary>The app stopped while the session was still running, so it never ended properly.</summary>
    Interrupted,
}

public sealed class Session
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int PlannedSeconds { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime? EndedUtc { get; set; }
    public EndReason? EndReason { get; set; }
    public List<Plan> Plans { get; set; } = [];
    public SessionClockState Clock { get; set; } = new();

    /// <summary>Regions picked out of the canvases for the PDF. Where each one goes is on the item.</summary>
    public List<ExtractItem> Extracts { get; set; } = [];

    /// <summary>The PDF's A4 pages, in order.</summary>
    public List<PdfPage> Pages { get; set; } = [];

    /// <summary>White pages instead of the canvas's dark ground.</summary>
    public bool PdfLight { get; set; }

    /// <summary>Each section's name printed above it.</summary>
    public bool PdfTitles { get; set; }

    /// <summary>Which of the two builds the PDF: free-layout pages or the flowing document.</summary>
    public string PdfMode { get; set; } = global::FocusLock.Core.Document.PdfMode.Free;

    public DocModel Document { get; set; } = new();

    public bool IsEnded => EndedUtc is not null;
}

/// <summary>
/// One region of one plan's canvas, marked for export. Only the rectangle is stored, never a
/// picture: the PDF is drawn from the canvas as it stands when you export it.
/// </summary>
public sealed class ExtractItem
{
    public string Id { get; set; } = "";
    public string PlanId { get; set; } = "";
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }

    /// <summary>The page it sits on; empty until it has been placed.</summary>
    public string PageId { get; set; } = "";

    /// <summary>Top-left of its box on the page, in points.</summary>
    public double PageX { get; set; }
    public double PageY { get; set; }

    /// <summary>Printed width in points. The height follows from the region's shape.</summary>
    public double PageW { get; set; }
}

public sealed class PdfPage
{
    public string Id { get; set; } = "";
    public bool Landscape { get; set; }
}

public sealed class Plan
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public string Status { get; set; } = "draft";
    public string Dot { get; set; } = "#eae7e1";
    public bool Done { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public BoardDoc Doc { get; set; } = new();
}

public sealed class SessionClockState
{
    public double ConfirmedElapsedSec { get; set; }
    public DateTime LastCheckpointUtc { get; set; }
}

/// <summary>Pointer to the session that currently holds the lock.</summary>
public sealed class ActiveSession
{
    public string SessionId { get; set; } = "";

    /// <summary>DisableTaskMgr was already set before we locked, so unlocking must leave it alone.</summary>
    public bool TaskMgrWasDisabled { get; set; }
}
