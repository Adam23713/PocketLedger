namespace PocketLedger.Security;

// The unlocked DEK for the current logical operation (one API request). EF value converters are compiled into the model and
// cannot receive scoped services, so they read the key from here. The scope owns the key: disposing it zeroes the material and
// detaches it from every execution context that captured it, including work that outlives the request.
public static class UserKeyContext
{
    private static readonly AsyncLocal<Holder?> current = new();

    public static UserDataKey? Current => current.Value?.Key;

    public static UserDataKey Required => Current ?? throw new UserKeyUnavailableException();

    public static IDisposable Enter(UserDataKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var holder = new Holder(key);
        var scope = new Scope(holder, current.Value);
        current.Value = holder;
        return scope;
    }

    private sealed class Holder(UserDataKey key)
    {
        public UserDataKey? Key { get; set; } = key;
    }

    private sealed class Scope(Holder holder, Holder? previous) : IDisposable
    {
        public void Dispose()
        {
            var key = holder.Key;
            if (key is null) return;
            holder.Key = null;
            if (ReferenceEquals(current.Value, holder)) current.Value = previous;
            key.Dispose();
        }
    }
}
