using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using PocketLedger.Security;

return args switch
{
    ["create-oci-credential"] => CreateCredential(),
    ["change-passphrase"] => ChangePassphrase(),
    ["unlock"] => await UnlockAsync("/tmp/pocketledger-unlock/unlock.sock"),
    ["unlock", "--socket", var socketPath] => await UnlockAsync(socketPath),
    ["readiness", var url] => await ReadinessAsync(url),
    _ => Usage()
};

static int CreateCredential()
{
    char[]? pemPassphrase = null;
    char[]? passphrase = null;
    char[]? confirmation = null;
    char[]? pemCharacters = null;
    byte[]? pemBytes = null;
    byte[]? encrypted = null;
    try
    {
        var pemPath = Prompt("Passphrase-protected OCI PEM path: ");
        var outputPath = Prompt("Encrypted credential output path: ");
        var tenancy = Prompt("OCI tenancy OCID: ");
        var user = Prompt("OCI user OCID: ");
        var fingerprint = Prompt("OCI API key fingerprint: ").ToLowerInvariant();
        var region = Prompt("OCI region (for example eu-frankfurt-1): ");
        pemPassphrase = ReadSecret("PEM passphrase (leave empty for an unencrypted PEM): ", allowEmpty: true);
        passphrase = ReadSecret("New credential passphrase: ");
        confirmation = ReadSecret("Confirm new credential passphrase: ");
        if (!passphrase.AsSpan().SequenceEqual(confirmation)) throw new InvalidOperationException("The new passphrases do not match.");

        pemBytes = File.ReadAllBytes(pemPath);
        pemCharacters = Encoding.UTF8.GetChars(pemBytes);
        using var rsa = RSA.Create();
        if (pemPassphrase.Length == 0) rsa.ImportFromPem(pemCharacters);
        else rsa.ImportFromEncryptedPem(pemCharacters, pemPassphrase);
        var actualFingerprint = Fingerprint(rsa);
        if (!string.Equals(actualFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException($"The PEM public-key fingerprint does not match the supplied OCI fingerprint (calculated {actualFingerprint}).");
        var pkcs8 = rsa.ExportPkcs8PrivateKey();
        try
        {
            using var credential = new OciCredential(tenancy, user, fingerprint, region, pkcs8);
            encrypted = OciCredentialProtection.Encrypt(credential, passphrase);
            OciCredentialProtection.WriteAtomic(outputPath, encrypted, replaceExisting: false);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(pkcs8);
            throw;
        }
        Console.WriteLine("Encrypted OCI credential created with mode 0600. The plaintext PEM was not copied.");
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Credential creation failed: {exception.Message}");
        return 1;
    }
    finally
    {
        Clear(pemPassphrase); Clear(passphrase); Clear(confirmation); Clear(pemCharacters);
        if (pemBytes is not null) CryptographicOperations.ZeroMemory(pemBytes);
        if (encrypted is not null) CryptographicOperations.ZeroMemory(encrypted);
    }
}

static int ChangePassphrase()
{
    char[]? oldPassphrase = null;
    char[]? newPassphrase = null;
    char[]? confirmation = null;
    try
    {
        var path = Prompt("Encrypted credential path: ");
        oldPassphrase = ReadSecret("Current passphrase: ");
        newPassphrase = ReadSecret("New passphrase: ");
        confirmation = ReadSecret("Confirm new passphrase: ");
        if (!newPassphrase.AsSpan().SequenceEqual(confirmation)) throw new InvalidOperationException("The new passphrases do not match.");
        OciCredentialProtection.ReencryptAtomic(path, oldPassphrase, newPassphrase);
        Console.WriteLine("Credential passphrase changed atomically.");
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Passphrase change failed; the original credential remains in place: {exception.Message}");
        return 1;
    }
    finally
    {
        Clear(oldPassphrase); Clear(newPassphrase); Clear(confirmation);
    }
}

static async Task<int> UnlockAsync(string socketPath)
{
    char[]? passphrase = null;
    byte[]? secret = null;
    try
    {
        passphrase = ReadSecret("Unlock passphrase: ");
        secret = Encoding.UTF8.GetBytes(passphrase);
        if (secret.Length > 4096) throw new InvalidOperationException("The passphrase is too long.");
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath));
        var request = new byte[4 + secret.Length];
        BinaryPrimitives.WriteInt32LittleEndian(request, secret.Length);
        secret.CopyTo(request, 4);
        try { await socket.SendAsync(request, SocketFlags.None); }
        finally { CryptographicOperations.ZeroMemory(request); }
        var header = new byte[5];
        await ReceiveExactlyAsync(socket, header);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(1));
        if (length is < 0 or > 4096) throw new InvalidDataException("Invalid response from the unlock socket.");
        var message = new byte[length];
        await ReceiveExactlyAsync(socket, message);
        Console.WriteLine(Encoding.UTF8.GetString(message));
        return header[0] == 1 ? 0 : 1;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Unlock failed: {exception.Message}");
        return 1;
    }
    finally
    {
        Clear(passphrase);
        if (secret is not null) CryptographicOperations.ZeroMemory(secret);
    }
}

static async Task ReceiveExactlyAsync(Socket socket, Memory<byte> buffer)
{
    var offset = 0;
    while (offset < buffer.Length)
    {
        var count = await socket.ReceiveAsync(buffer[offset..], SocketFlags.None);
        if (count == 0) throw new EndOfStreamException("The unlock socket closed unexpectedly.");
        offset += count;
    }
}

static async Task<int> ReadinessAsync(string url)
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var endpoint) || !endpoint.IsLoopback || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)) return 2;
    using var handler = new HttpClientHandler();
    if (endpoint.Scheme == Uri.UriSchemeHttps) handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
    try { return (await client.GetAsync(endpoint)).IsSuccessStatusCode ? 0 : 1; }
    catch { return 1; }
}

static string Fingerprint(RSA rsa)
{
    var publicKey = rsa.ExportSubjectPublicKeyInfo();
    try { return string.Join(':', MD5.HashData(publicKey).Select(value => value.ToString("x2"))); }
    finally { CryptographicOperations.ZeroMemory(publicKey); }
}

static string Prompt(string prompt)
{
    Console.Write(prompt);
    return Console.ReadLine()?.Trim() ?? throw new EndOfStreamException("Interactive input ended unexpectedly.");
}

static char[] ReadSecret(string prompt, bool allowEmpty = false)
{
    if (Console.IsInputRedirected) throw new InvalidOperationException("Secret input requires an interactive terminal.");
    Console.Write(prompt);
    var result = new List<char>();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace)
        {
            if (result.Count > 0) result.RemoveAt(result.Count - 1);
            continue;
        }
        if (!char.IsControl(key.KeyChar)) result.Add(key.KeyChar);
    }
    Console.WriteLine();
    if (!allowEmpty && result.Count == 0) throw new InvalidOperationException("The passphrase must not be empty.");
    var secret = result.ToArray();
    CollectionsMarshal.AsSpan(result).Clear();
    return secret;
}

static void Clear(char[]? value)
{
    if (value is not null) Array.Clear(value);
}

static int Usage()
{
    Console.Error.WriteLine("Usage: pocketledger-security <create-oci-credential|change-passphrase|unlock [--socket PATH]|readiness LOOPBACK_URL>");
    return 2;
}
