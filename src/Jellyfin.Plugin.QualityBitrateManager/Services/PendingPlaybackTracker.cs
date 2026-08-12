using System.Collections.Concurrent;

namespace Jellyfin.Plugin.QualityBitrateManager.Services;

public sealed record PendingPlayback(string Key, Guid UserId, Guid ItemId, long Limit, DateTimeOffset ExpiresAt);

public sealed class PendingPlaybackTracker
{
    private readonly ConcurrentDictionary<Guid, UserPendingState> _users = new();

    public PendingPlayback Add(Guid userId, Guid itemId, long limit, TimeSpan lifetime)
    {
        var entry = new PendingPlayback(Guid.NewGuid().ToString("N"), userId, itemId, limit, DateTimeOffset.UtcNow.Add(lifetime));
        var state = _users.GetOrAdd(userId, static _ => new());
        lock (state.Gate) state.Entries[entry.Key] = entry;
        return entry;
    }

    public void Remove(Guid userId, string key)
    {
        if (!_users.TryGetValue(userId, out var state)) return;
        lock (state.Gate) { state.Entries.Remove(key); RemoveEmpty(userId, state); }
    }

    public void Consume(Guid userId, Guid itemId)
    {
        if (!_users.TryGetValue(userId, out var state)) return;
        lock (state.Gate)
        {
            var entry = state.Entries.Values.Where(x => x.ItemId == itemId).OrderBy(x => x.ExpiresAt).FirstOrDefault();
            if (entry is not null) state.Entries.Remove(entry.Key);
            RemoveEmpty(userId, state);
        }
    }

    public long? GetEffectiveLimit(Guid userId)
    {
        if (!_users.TryGetValue(userId, out var state)) return null;
        lock (state.Gate) return state.Entries.Count == 0 ? null : state.Entries.Values.Min(x => x.Limit);
    }

    public IReadOnlyList<Guid> RemoveExpired(DateTimeOffset now)
    {
        var affected = new List<Guid>();
        foreach (var pair in _users)
        {
            lock (pair.Value.Gate)
            {
                var expired = pair.Value.Entries.Values.Where(x => x.ExpiresAt <= now).Select(x => x.Key).ToArray();
                foreach (var key in expired) pair.Value.Entries.Remove(key);
                if (expired.Length > 0) affected.Add(pair.Key);
                RemoveEmpty(pair.Key, pair.Value);
            }
        }
        return affected;
    }

    public IReadOnlyList<Guid> UserIds => _users.Keys.ToArray();
    public void Clear() => _users.Clear();

    private void RemoveEmpty(Guid userId, UserPendingState state)
    {
        if (state.Entries.Count == 0) _users.TryRemove(new KeyValuePair<Guid, UserPendingState>(userId, state));
    }

    private sealed class UserPendingState { public object Gate { get; } = new(); public Dictionary<string, PendingPlayback> Entries { get; } = []; }
}
