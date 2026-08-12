using System.Collections.Concurrent;

namespace Jellyfin.Plugin.QualityBitrateManager.Services;

public sealed class UserOperationCoordinator
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();

    public async Task RunAsync(Guid userId, Func<Task> action)
    {
        var gate = _gates.GetOrAdd(userId, static _ => new(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        try { await action().ConfigureAwait(false); }
        finally { gate.Release(); }
    }
}
