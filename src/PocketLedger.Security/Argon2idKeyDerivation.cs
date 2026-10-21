using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace PocketLedger.Security;

internal static class Argon2idKeyDerivation
{
    public const int KeySize = 32;
    public const int MinimumMemoryKiB = 32 * 1024;
    public const int MaximumMemoryKiB = 256 * 1024;

    // Bounds are checked before derivation so stored parameters cannot be used for resource exhaustion.
    public static bool AreParametersSupported(int memoryKiB, int iterations, int parallelism)
        => memoryKiB is >= MinimumMemoryKiB and <= MaximumMemoryKiB && iterations is >= 2 and <= 10 && parallelism is >= 1 and <= 8;

    public static byte[] DeriveKey(ReadOnlySpan<char> secret, ReadOnlySpan<byte> salt, int memoryKiB, int iterations, int parallelism)
    {
        if (!AreParametersSupported(memoryKiB, iterations, parallelism)) throw new ArgumentOutOfRangeException(nameof(memoryKiB), "Unsupported Argon2id parameters.");
        var secretBytes = new byte[Encoding.UTF8.GetByteCount(secret)];
        Encoding.UTF8.GetBytes(secret, secretBytes);
        var saltCopy = salt.ToArray();
        try
        {
            using var argon2 = new Argon2id(secretBytes) { Salt = saltCopy, MemorySize = memoryKiB, Iterations = iterations, DegreeOfParallelism = parallelism };
            return argon2.GetBytes(KeySize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
            CryptographicOperations.ZeroMemory(saltCopy);
        }
    }
}
