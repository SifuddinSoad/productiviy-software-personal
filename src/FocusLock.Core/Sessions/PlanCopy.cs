using System.Text.Json;
using FocusLock.Core.Models;

namespace FocusLock.Core.Sessions;

public static class PlanCopy
{
    /// <summary>Deep copy of a plan (canvas included) for a new session: new id, not done.</summary>
    public static Plan ForNewSession(Plan source)
    {
        var copy = JsonSerializer.Deserialize<Plan>(JsonSerializer.SerializeToUtf8Bytes(source, Json.Options), Json.Options)!;
        copy.Id = Ids.New("pl");
        copy.Done = false;
        copy.UpdatedUtc = DateTime.UtcNow;
        return copy;
    }

    public static BoardDoc CloneDoc(BoardDoc doc) =>
        JsonSerializer.Deserialize<BoardDoc>(JsonSerializer.SerializeToUtf8Bytes(doc, Json.Options), Json.Options)!;
}
