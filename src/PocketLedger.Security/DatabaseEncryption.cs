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
