using FocusLock.Core.Sessions;

namespace FocusLock.Core.Guard;

/// <summary>
/// The guard's files: one state file the service owns, and an inbox the app drops commands into.
///
/// Every read is forgiving. The service runs before anyone can look at a log, so a damaged file has
/// to mean "nothing is being guarded" rather than a crash loop with the screen held.
/// </summary>
public sealed class GuardStore(string? root = null)
{
    readonly string _root = root ?? GuardPaths.Root;

    public string Root => _root;

    public GuardState? Read() => Json.Read<GuardState>(GuardPaths.StateFile(_root));

    public void Write(GuardState state) => Json.WriteAtomic(GuardPaths.StateFile(_root), state);

    public void Clear()
    {
        var path = GuardPaths.StateFile(_root);
        foreach (var file in new[] { path, path + ".bak", path + ".tmp" })
            TryDelete(file);
    }

    /// <summary>App side: leave a command for the service. Never throws — the guard is an extra,
    /// and a session must still run when the service is not installed.</summary>
    public bool Send(GuardCommand command)
    {
        try
        {
            // Only the installer creates the inbox. With no service to read them, writing commands
            // would do nothing but fill ProgramData with a file every fifteen seconds.
            var dir = GuardPaths.InboxDir(_root);
            if (!Directory.Exists(dir)) return false;

            Json.WriteAtomic(Path.Combine(dir, $"{Guid.NewGuid():N}.json"), command);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Service side: everything waiting, oldest first, removed as it is read.</summary>
    public IReadOnlyList<GuardCommand> Drain()
    {
        var dir = GuardPaths.InboxDir(_root);
        if (!Directory.Exists(dir)) return [];

        var commands = new List<GuardCommand>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.json").OrderBy(File.GetCreationTimeUtc))
        {
            var command = Json.Read<GuardCommand>(file);
            TryDelete(file);
            TryDelete(file + ".bak");
            if (command is not null) commands.Add(command);
        }
        return commands;
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // a file we cannot remove is not worth failing over
        }
    }
}
