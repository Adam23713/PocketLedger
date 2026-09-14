using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;

var directory = Path.Combine(Path.GetTempPath(), "pocketledger-encryption-benchmark-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
using var rsa = RSA.Create(3072);
var request = new CertificateRequest("CN=PocketLedger synthetic benchmark", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
var provider = DataProtectionProvider.Create(new DirectoryInfo(directory), options => options.SetApplicationName("PocketLedger.EncryptionBenchmark").ProtectKeysWithCertificate(certificate));
var protector = provider.CreateProtector("PocketLedger.Database.v1", "Transaction.Note");
Console.WriteLine($"Runtime: {Environment.Version}; OS: {Environment.OSVersion}; CPU count: {Environment.ProcessorCount}");
Console.WriteLine("Synthetic in-process benchmark; excludes database I/O, EF, network and LUKS overhead.");
foreach (var length in new[] { 100, 500, 10000 })
{
    var plaintext = new string('á', length);
    var encrypted = protector.Protect(plaintext);
    for (var i = 0; i < 1000; i++) protector.Unprotect(protector.Protect(plaintext));
    const int iterations = 10000;
    Measure($"protect {length} chars", () => protector.Protect(plaintext));
    Measure($"unprotect {length} chars", () => protector.Unprotect(encrypted));
    Measure($"search {length} chars", () => protector.Unprotect(encrypted).Contains("missing", StringComparison.OrdinalIgnoreCase));
    Console.WriteLine($"UTF-8 plaintext bytes: {System.Text.Encoding.UTF8.GetByteCount(plaintext)}; stored ciphertext chars: {encrypted.Length}");
    void Measure(string label, Action operation)
    {
        GC.Collect();
        var allocation = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++) operation();
        timer.Stop();
        Console.WriteLine($"{label}: {timer.Elapsed.TotalMilliseconds:F2} ms / {iterations} operations; {(GC.GetAllocatedBytesForCurrentThread() - allocation) / iterations} bytes allocated / operation");
    }
}
// Contains synthetic, disposable benchmark material only; never points at application keys.
Directory.Delete(directory, recursive: true);
