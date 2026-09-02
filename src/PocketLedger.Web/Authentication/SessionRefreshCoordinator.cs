using System.Collections.Concurrent;

namespace PocketLedger.Web.Authentication;

public sealed class SessionRefreshCoordinator
{
    private readonly ConcurrentDictionary<string, Entry> entries = new();

    internal int TrackedSessionCount => entries.Count;

    public async ValueTask<IDisposable> AcquireAsync(string sessionKey, CancellationToken cancellationToken)
    {
        Entry entry;
        while (true)
        {
            entry = entries.GetOrAdd(sessionKey, static _ => new Entry());
            lock (entry.SyncRoot)
            {
                if (entry.Retired) continue;
                entry.ReferenceCount++;
                break;
            }
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
            return new Releaser(this, sessionKey, entry);
        }
        catch
        {
            ReleaseReference(sessionKey, entry);
            throw;
        }
    }

    private void Release(string sessionKey, Entry entry)
    {
        entry.Semaphore.Release();
        ReleaseReference(sessionKey, entry);
    }

    private void ReleaseReference(string sessionKey, Entry entry)
    {
        lock (entry.SyncRoot)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount != 0) return;
            entry.Retired = true;
            entries.TryRemove(new KeyValuePair<string, Entry>(sessionKey, entry));
        }
    }

    private sealed class Entry
    {
        public object SyncRoot { get; } = new();
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int ReferenceCount { get; set; }
        public bool Retired { get; set; }
    }

    private sealed class Releaser(SessionRefreshCoordinator owner, string sessionKey, Entry entry) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            owner.Release(sessionKey, entry);
        }
    }
}
