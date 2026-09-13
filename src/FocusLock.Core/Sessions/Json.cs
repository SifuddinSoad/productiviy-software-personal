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
        WriteIndented = false,
    };

    /// <summary>Write via a temp file and rename, so a crash mid-write never leaves a half file.</summary>
    public static void WriteAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(value, Options));
        File.Move(tmp, path, overwrite: true);
    }

    static ReadOnlySpan<byte> Bom => [0xEF, 0xBB, 0xBF];

    public static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            var bytes = File.ReadAllBytes(path).AsSpan();
            // A file hand-edited in Notepad comes back with a BOM, which the JSON reader rejects.
            if (bytes.StartsWith(Bom)) bytes = bytes[3..];
            return JsonSerializer.Deserialize<T>(bytes, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
