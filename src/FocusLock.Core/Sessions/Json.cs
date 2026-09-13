using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusLock.Core.Sessions;

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        // a document block's "kind" need not come first, so a hand-edited file still loads
        AllowOutOfOrderMetadataProperties = true,
        WriteIndented = false,
    };

    /// <summary>
    /// Writes through a temp file so a crash mid-write never leaves half a file.
    ///
    /// The bytes are forced to the disk before the rename: renaming is journaled but the contents
    /// are not, so a machine reset moments after a plain write leaves a correctly named file full
    /// of zeros. The previous copy is kept alongside as .bak, which <see cref="Read"/> falls back
    /// to if the newest one is unreadable.
    /// </summary>
    public static void WriteAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        var backup = path + ".bak";

        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(JsonSerializer.SerializeToUtf8Bytes(value, Options));
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(path)) File.Replace(tmp, path, backup, ignoreMetadataErrors: true);
        else File.Move(tmp, path);
    }

    static ReadOnlySpan<byte> Bom => [0xEF, 0xBB, 0xBF];

    public static T? Read<T>(string path) where T : class =>
        ReadOne<T>(path) ?? ReadOne<T>(path + ".bak");

    static T? ReadOne<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            var bytes = File.ReadAllBytes(path).AsSpan();
            // A file hand-edited in Notepad comes back with a BOM, which the JSON reader rejects.
            if (bytes.StartsWith(Bom)) bytes = bytes[3..];
            return JsonSerializer.Deserialize<T>(bytes, Options);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
