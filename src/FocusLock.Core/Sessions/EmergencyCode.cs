using System.Security.Cryptography;
using System.Text;

namespace FocusLock.Core.Sessions;

public static class EmergencyCode
{
    // No look-alike characters (0/O, 1/l/I) so the only hard part is the length.
    const string Alphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>Random groups of 5 characters separated by single spaces.</summary>
    public static string Generate(int characters = 200)
    {
        var sb = new StringBuilder(characters + characters / 5);
        for (var i = 0; i < characters; i++)
        {
            if (i > 0 && i % 5 == 0) sb.Append(' ');
            sb.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
        }
        return sb.ToString();
    }

    /// <summary>Exact, case-sensitive. Only surrounding whitespace is forgiven.</summary>
    public static bool Matches(string code, string input) =>
        string.Equals(code, input.Trim(), StringComparison.Ordinal);
}
