using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace PocketLedger.Security;

// 30 Crockford base32 symbols (150 bits), shown once as six hyphenated groups of five.
public static class RecoveryKey
{
    internal const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    internal const int GroupCount = 6;
    internal const int GroupLength = 5;
    internal const int Length = GroupCount * GroupLength;

    public static string Generate()
    {
        var builder = new StringBuilder(Length + GroupCount - 1);
        for (var index = 0; index < Length; index++)
        {
            if (index > 0 && index % GroupLength == 0) builder.Append('-');
            builder.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
        }
        return builder.ToString();
    }

    // Accepts the displayed form with any casing, spacing or hyphenation, and the usual Crockford look-alikes (O→0, I/L→1).
    public static bool TryNormalize(ReadOnlySpan<char> input, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;
        Span<char> buffer = stackalloc char[Length];
        var count = 0;
        foreach (var raw in input)
        {
            if (raw == '-' || char.IsWhiteSpace(raw)) continue;
            var character = char.ToUpperInvariant(raw) switch { 'O' => '0', 'I' or 'L' => '1', var other => other };
            if (count == Length || !Alphabet.Contains(character)) return false;
            buffer[count++] = character;
        }
        if (count != Length) return false;
        normalized = new string(buffer);
        return true;
    }
}
