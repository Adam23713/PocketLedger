using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace PocketLedger.Security;

public sealed class EncryptionRuntimeState
{
    private int ready;
    public EncryptionRuntimeState(bool requiresUnlock) { RequiresUnlock = requiresUnlock; ready = requiresUnlock ? 0 : 1; }
    public bool RequiresUnlock { get; }
    public bool IsReady => Volatile.Read(ref ready) == 1;
    internal void MarkReady() => Volatile.Write(ref ready, 1);
}

internal sealed class LockedOciKeyEncryptionProvider : IKeyEncryptionProvider, IDisposable
{
    private readonly OciVaultOptions options;
    private readonly EncryptionRuntimeState state;
    private readonly Func<OciVaultOptions, OciCredential, OciKmsKeyEncryptionProvider> factory;
    private readonly SemaphoreSlim unlockLock = new(1, 1);
    private OciKmsKeyEncryptionProvider? active;

    public LockedOciKeyEncryptionProvider(OciVaultOptions options, EncryptionRuntimeState state, Func<OciVaultOptions, OciCredential, OciKmsKeyEncryptionProvider>? factory = null)
    {
        this.options = options;
        this.state = state;
        this.factory = factory ?? ((configuredOptions, credential) => new OciKmsKeyEncryptionProvider(configuredOptions, credential));
    }

    public string Name => EncryptionProviderNames.OciVault;

    public Microsoft.AspNetCore.DataProtection.XmlEncryption.EncryptedXmlInfo Encrypt(XElement plaintextElement)
        => Volatile.Read(ref active)?.Encrypt(plaintextElement) ?? throw new CryptographicException("OCI KMS is locked. Manual unlock is required.");

    internal XElement Decrypt(XElement encryptedElement)
        => Volatile.Read(ref active)?.Decrypt(encryptedElement) ?? throw new CryptographicException("OCI KMS is locked. Manual unlock is required.");

    public async Task UnlockAsync(ReadOnlyMemory<char> passphrase, CancellationToken cancellationToken, Action? verifyApplication = null)
    {
        await unlockLock.WaitAsync(cancellationToken);
        try
        {
            if (active is not null) return;
            var encrypted = OciCredentialProtection.ReadPrivateFile(options.EncryptedCredentialPath);
            try
            {
                using var credential = OciCredentialProtection.Decrypt(encrypted, passphrase.Span);
                var candidate = factory(options, credential);
                try
                {
                    var probe = new XElement("unlockProbe", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
                    var wrapped = candidate.Encrypt(probe);
                    if (!XNode.DeepEquals(probe, candidate.Decrypt(wrapped.EncryptedElement))) throw new CryptographicException("OCI KMS credential verification failed.");
                    Volatile.Write(ref active, candidate);
                    try
                    {
                        verifyApplication?.Invoke();
                        state.MarkReady();
                    }
                    catch
                    {
                        Interlocked.CompareExchange(ref active, null, candidate);
                        throw;
                    }
                }
                catch
                {
                    candidate.Dispose();
                    throw;
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(encrypted);
            }
        }
        finally
        {
            unlockLock.Release();
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref active, null)?.Dispose();
        unlockLock.Dispose();
    }
}

internal sealed class OciUnlockSocketService(LockedOciKeyEncryptionProvider provider, OciVaultOptions options, IDataProtectionProvider dataProtection, KeyRingStorageOptions storage, ILogger<OciUnlockSocketService> logger) : BackgroundService
{
    private const int MaximumPassphraseBytes = 4096;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var directory = Path.GetDirectoryName(options.UnlockSocketPath) ?? throw new InvalidOperationException("The OCI unlock socket path has no parent directory.");
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (File.Exists(options.UnlockSocketPath)) File.Delete(options.UnlockSocketPath);
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(options.UnlockSocketPath));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(options.UnlockSocketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        listener.Listen(4);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var client = await listener.AcceptAsync(stoppingToken);
                await HandleAsync(client, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            if (File.Exists(options.UnlockSocketPath)) File.Delete(options.UnlockSocketPath);
        }
    }

    private async Task HandleAsync(Socket client, CancellationToken cancellationToken)
    {
        byte[]? secretBytes = null;
        char[]? passphrase = null;
        try
        {
            var header = new byte[4];
            await ReceiveExactlyAsync(client, header, cancellationToken);
            var length = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (length is <= 0 or > MaximumPassphraseBytes) throw new InvalidDataException("Invalid unlock request.");
            secretBytes = new byte[length];
            await ReceiveExactlyAsync(client, secretBytes, cancellationToken);
            passphrase = Encoding.UTF8.GetChars(secretBytes);
            await provider.UnlockAsync(passphrase, cancellationToken, () => EncryptionReadinessCheck.Verify(dataProtection, storage));
            await SendResponseAsync(client, true, "OCI KMS unlocked and verified.", cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("OCI KMS unlock failed; the service remains locked: {ErrorType}", exception.GetType().Name);
            await SendResponseAsync(client, false, "Unlock failed. Check the passphrase, credential file, OCI access, endpoint and Key OCID.", cancellationToken);
        }
        finally
        {
            if (secretBytes is not null) CryptographicOperations.ZeroMemory(secretBytes);
            if (passphrase is not null) Array.Clear(passphrase);
        }
    }

    private static async Task ReceiveExactlyAsync(Socket socket, Memory<byte> buffer, CancellationToken token)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var count = await socket.ReceiveAsync(buffer[offset..], SocketFlags.None, token);
            if (count == 0) throw new EndOfStreamException("The unlock request ended unexpectedly.");
            offset += count;
        }
    }

    private static async Task SendResponseAsync(Socket socket, bool success, string message, CancellationToken token)
    {
        var text = Encoding.UTF8.GetBytes(message);
        var response = new byte[5 + text.Length];
        response[0] = success ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(response.AsSpan(1), text.Length);
        text.CopyTo(response, 5);
        await socket.SendAsync(response, SocketFlags.None, token);
    }
}

public static class EncryptionRuntimeExtensions
{
    public static IApplicationBuilder UseEncryptionReadinessGate(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var state = context.RequestServices.GetRequiredService<EncryptionRuntimeState>();
            if (!state.IsReady && context.Request.Path != "/health/live" && context.Request.Path != "/health/ready")
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(new { status = "locked" });
                return;
            }
            await next(context);
        });
    }

    public static IEndpointRouteBuilder MapEncryptionHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "alive" })).AllowAnonymous().ExcludeFromDescription();
        endpoints.MapGet("/health/ready", (EncryptionRuntimeState state) => state.IsReady ? Results.Ok(new { status = "ready" }) : Results.Json(new { status = "locked" }, statusCode: StatusCodes.Status503ServiceUnavailable)).AllowAnonymous().ExcludeFromDescription();
        return endpoints;
    }
}
