using FocusLock.Core.Models;

namespace FocusLock.Core.Sessions;

public sealed class SessionStore(string directory)
{
    public string Directory { get; } = directory;

    string PathFor(string id) => System.IO.Path.Combine(Directory, id + ".json");

    public void Save(Session session) => Json.WriteAtomic(PathFor(session.Id), session);

    public Session? Load(string id) => Json.Read<Session>(PathFor(id));

    /// <summary>
    /// Closes out sessions left unfinished by a crash or by the app being killed, so they stop
    /// showing as active forever. The end time is the last moment the session was known to be alive.
    /// </summary>
    public int CloseUnfinished(string? exceptId)
    {
        var closed = 0;
        foreach (var s in List().Where(s => !s.IsEnded && s.Id != exceptId))
        {
            s.EndedUtc = s.Clock.LastCheckpointUtc == default ? s.StartUtc : s.Clock.LastCheckpointUtc;
            s.EndReason = EndReason.Interrupted;
            Save(s);
            closed++;
        }
        return closed;
    }

    /// <summary>All readable sessions, newest first. Corrupt files are skipped.</summary>
    public List<Session> List()
    {
        if (!System.IO.Directory.Exists(Directory)) return [];
        return System.IO.Directory.EnumerateFiles(Directory, "*.json")
            .Where(f => System.IO.Path.GetExtension(f).Equals(".json", StringComparison.OrdinalIgnoreCase))
            .Select(Json.Read<Session>)
            .OfType<Session>()
            .OrderByDescending(s => s.StartUtc)
            .ToList();
    }
}

public sealed class ActiveSessionStore(string path)
{
    public ActiveSession? Get() => Json.Read<ActiveSession>(path);

    public void Set(ActiveSession active) => Json.WriteAtomic(path, active);

    public void Clear()
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
