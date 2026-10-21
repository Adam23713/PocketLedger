using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PocketLedger.Security;

public sealed class DatabaseEncryption(IDataProtectionProvider provider)
{
    public IDataProtector CreateProtector(string purpose) => provider.CreateProtector("PocketLedger.Database.v1", purpose);

    public void Configure(PropertyBuilder property, string purpose, int? maximumPlaintextLength = null)
    {
        var protector = CreateProtector(purpose);
        property.HasConversion(new ValueConverter<string, string>(value => Protect(protector, value, maximumPlaintextLength), value => protector.Unprotect(value)));
    }
    // User data: an inner AES-GCM layer with the signed-in user's DEK (UserKeyContext) under the host's Data Protection layer.
    // Reading or writing without an unlocked user key fails; there is no fallback to the host key alone.
    public void ConfigureUserField(PropertyBuilder property, string purpose, string fieldGroup, int? maximumPlaintextLength = null)
    {
        var protector = CreateUserFieldProtector(purpose);
        property.HasConversion(new ValueConverter<string, string>(
            value => protector.Protect(UserFieldCipher.EncryptString(CheckLength(value, maximumPlaintextLength), UserKeyContext.Required, fieldGroup)),
            value => UserFieldCipher.DecryptString(protector.Unprotect(value), UserKeyContext.Required, fieldGroup)));
    }

    // For background work that has no user key: the property holds the inner per-user ciphertext, which can be copied
    // between fields of the same group and user but never read.
    public void ConfigureUserFieldCiphertext(PropertyBuilder property, string purpose)
    {
        var protector = CreateUserFieldProtector(purpose);
        property.HasConversion(new ValueConverter<string, string>(value => protector.Protect(value), value => protector.Unprotect(value)));
    }

    private IDataProtector CreateUserFieldProtector(string purpose) => provider.CreateProtector("PocketLedger.Database.v2", purpose);

    private static string CheckLength(string value, int? maximumPlaintextLength)
        => maximumPlaintextLength is { } limit && value.Length > limit ? throw new InvalidOperationException($"The encrypted field exceeds its {limit} character limit.") : value;

    private static string Protect(IDataProtector protector, string value, int? maximumPlaintextLength)
    {
        if (maximumPlaintextLength is { } limit && value.Length > limit)
            throw new InvalidOperationException($"The encrypted field exceeds its {limit} character limit.");
        return protector.Protect(value);
    }
}

public interface IEncryptedDbContext
{
    DatabaseEncryption? Encryption { get; }
}

// EF models capture their converters. Never reuse a model with another host's key provider.
public sealed class EncryptionModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => (context.GetType(), (context as IEncryptedDbContext)?.Encryption, designTime);
}
