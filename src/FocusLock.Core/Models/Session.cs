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

    public bool IsEnded => EndedUtc is not null;
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
