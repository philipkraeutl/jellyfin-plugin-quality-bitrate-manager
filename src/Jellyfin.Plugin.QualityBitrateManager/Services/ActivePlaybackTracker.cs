using System.Collections.Concurrent;
using Jellyfin.Plugin.QualityBitrateManager.Models;

namespace Jellyfin.Plugin.QualityBitrateManager.Services;

public sealed class ActivePlaybackTracker
{
    private readonly ConcurrentDictionary<Guid, UserState> _users = new();

    public (long EffectiveLimit, int Count) Upsert(Guid userId, ActivePlayback playback)
    {
        var state = _users.GetOrAdd(userId, static _ => new());
        lock (state.Gate)
        {
            state.Streams[playback.PlaybackKey] = playback;
            return (state.Streams.Values.Min(x => x.RequiredLimit), state.Streams.Count);
        }
    }

    public (long? EffectiveLimit, int Count) Remove(Guid userId, string key)
    {
        if (!_users.TryGetValue(userId, out var state)) return (null, 0);
        lock (state.Gate)
        {
            state.Streams.Remove(key);
            var result = state.Streams.Count == 0 ? ((long?)null, 0) : (state.Streams.Values.Min(x => x.RequiredLimit), state.Streams.Count);
            if (state.Streams.Count == 0) _users.TryRemove(new KeyValuePair<Guid, UserState>(userId, state));
            return result;
        }
    }

    public IReadOnlyList<Guid> RemoveSession(string sessionId)
    {
        var affected = new List<Guid>();
        foreach (var pair in _users)
        {
            lock (pair.Value.Gate)
            {
                if (pair.Value.Streams.RemoveWhere(x => x.Value.SessionId == sessionId) > 0) affected.Add(pair.Key);
                if (pair.Value.Streams.Count == 0) _users.TryRemove(new KeyValuePair<Guid, UserState>(pair.Key, pair.Value));
            }
        }
        return affected;
    }

    public long? GetEffectiveLimit(Guid userId) => _users.TryGetValue(userId, out var state) ? GetLimit(state) : null;
    public IReadOnlyList<Guid> UserIds => _users.Keys.ToArray();
    public void Clear() => _users.Clear();

    private static long? GetLimit(UserState state) { lock (state.Gate) return state.Streams.Count == 0 ? null : state.Streams.Values.Min(x => x.RequiredLimit); }
    private sealed class UserState { public object Gate { get; } = new(); public Dictionary<string, ActivePlayback> Streams { get; } = new(StringComparer.Ordinal); }
}

internal static class DictionaryExtensions
{
    public static int RemoveWhere<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, Func<KeyValuePair<TKey, TValue>, bool> predicate) where TKey : notnull
    {
        var keys = dictionary.Where(predicate).Select(x => x.Key).ToArray();
        foreach (var key in keys) dictionary.Remove(key);
        return keys.Length;
    }
}
