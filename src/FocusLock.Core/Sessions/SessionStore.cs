using FocusLock.Core.Models;

namespace FocusLock.Core.Sessions;

public sealed class SessionStore(string directory)
{
    public string Directory { get; } = directory;

    string PathFor(string id) => System.IO.Path.Combine(Directory, id + ".json");

    public void Save(Session session) => Json.WriteAtomic(PathFor(session.Id), session);

    public Session? Load(string id) => Json.Read<Session>(PathFor(id));

    /// <summary>All readable sessions, newest first. Corrupt files are skipped.</summary>
    public List<Session> List()
    {
        if (!System.IO.Directory.Exists(Directory)) return [];
        return System.IO.Directory.EnumerateFiles(Directory, "*.json")
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
