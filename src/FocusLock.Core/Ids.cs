using System.Security.Cryptography;

namespace FocusLock.Core;

public static class Ids
{
    const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>Short random id like the design's <c>'o' + Math.random().toString(36).slice(2, 8)</c>.</summary>
    public static string New(string prefix = "o", int length = 6) =>
        prefix + RandomNumberGenerator.GetString(Alphabet, length);
}
