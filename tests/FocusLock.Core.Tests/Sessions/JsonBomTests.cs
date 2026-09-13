using System.Text;
using FocusLock.Core.Models;
using FocusLock.Core.Sessions;

namespace FocusLock.Core.Tests.Sessions;

public sealed class JsonBomTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "focuslock-bom-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void A_session_file_saved_with_a_utf8_bom_still_loads()
    {
        Directory.CreateDirectory(_dir);
        var json = """{"id":"s1","name":"Edited in Notepad","plannedSeconds":60,"plans":[]}""";
        File.WriteAllText(Path.Combine(_dir, "s1.json"), json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var loaded = new SessionStore(_dir).Load("s1");

        Assert.Equal("Edited in Notepad", loaded!.Name);
    }

    [Fact]
    public void Genuinely_broken_json_is_still_skipped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "s1.json"), "{ not json");

        Assert.Null(new SessionStore(_dir).Load("s1"));
    }
}
