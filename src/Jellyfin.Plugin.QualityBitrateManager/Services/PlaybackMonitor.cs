using Jellyfin.Plugin.QualityBitrateManager.Helpers;
using Jellyfin.Plugin.QualityBitrateManager.Models;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.QualityBitrateManager.Services;

public sealed class PlaybackMonitor(
    ISessionManager sessions,
    IMediaSourceManager mediaSources,
    ActivePlaybackTracker tracker,
    PendingPlaybackTracker pending,
    UserOperationCoordinator coordinator,
    BitratePolicyService policy,
    UserBitrateService users,
    ILogger<PlaybackMonitor> logger) : IHostedService
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int? Width, int? Height)> _sourceDimensions = new();
    private readonly CancellationTokenSource _cleanupCancellation = new();
    private Task? _cleanupTask;
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        sessions.PlaybackStart += OnPlaybackStart;
        sessions.PlaybackProgress += OnPlaybackProgress;
        sessions.PlaybackStopped += OnPlaybackStopped;
        sessions.SessionEnded += OnSessionEnded;
        _cleanupTask = CleanupPendingAsync(_cleanupCancellation.Token);

        // In-memory playback state cannot survive a restart. Reset every user currently known
        // through sessions; subsequent playback events reconstruct the conservative state.
        foreach (var userId in users.UserIds)
            await SetDefaultAsync(userId).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        sessions.PlaybackStart -= OnPlaybackStart;
        sessions.PlaybackProgress -= OnPlaybackProgress;
        sessions.PlaybackStopped -= OnPlaybackStopped;
        sessions.SessionEnded -= OnSessionEnded;
        await _cleanupCancellation.CancelAsync().ConfigureAwait(false);
        if (_cleanupTask is not null) await _cleanupTask.ConfigureAwait(false);
        foreach (var userId in tracker.UserIds.Concat(pending.UserIds).Distinct()) await SetDefaultAsync(userId).ConfigureAwait(false);
        tracker.Clear();
        pending.Clear();
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e) => _ = HandleStartOrProgressAsync(e);
    private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e) => _ = HandleStartOrProgressAsync(e);
    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e) => _ = HandleStopAsync(e);
    private void OnSessionEnded(object? sender, SessionEventArgs e) => _ = HandleSessionEndAsync(e.SessionInfo.Id);

    private async Task HandleStartOrProgressAsync(PlaybackProgressEventArgs e)
    {
        try
        {
            if (e.Users is null || e.Users.Count == 0 || e.Item?.MediaType != Jellyfin.Data.Enums.MediaType.Video) return;
            var key = GetKey(e.PlaySessionId, e.Session.Id, e.Item.Id);
            var dimensions = await GetSourceDimensionsAsync(e, key).ConfigureAwait(false);
            var quality = QualityClassifier.Classify(dimensions.Width, dimensions.Height);
            var config = Plugin.Instance?.Configuration;
            if (config is null) return;
            var limit = policy.GetLimit(quality, config);
            foreach (var user in e.Users)
            {
                (long EffectiveLimit, int Count) result;
                pending.Consume(user.Id, e.Item.Id);
                result = tracker.Upsert(user.Id, new(key, e.Session.Id, e.Item.Id, quality, limit, DateTimeOffset.UtcNow));
                var desired = 0L;
                await coordinator.RunAsync(user.Id, () => users.SetAsync(user.Id, desired = Min(tracker.GetEffectiveLimit(user.Id), pending.GetEffectiveLimit(user.Id), GetDefault()))).ConfigureAwait(false);
                logger.LogInformation("Playback active. User={UserId} Session={SessionId} Item={ItemId} SourceResolution={Width}x{Height} Quality={Quality} ConfiguredLimit={ConfiguredLimit} EffectiveUserLimit={EffectiveLimit}", user.Id, e.Session.Id, e.Item.Id, dimensions.Width, dimensions.Height, quality, limit, result.EffectiveLimit);
            }
        }
        catch (Exception ex) { logger.LogError(ex, "Failed to process playback event"); }
    }

    private async Task HandleStopAsync(PlaybackStopEventArgs e)
    {
        try
        {
            if (e.Users is null || e.Item is null) return;
            var key = GetKey(e.PlaySessionId, e.Session.Id, e.Item.Id);
            _sourceDimensions.TryRemove(key, out _);
            foreach (var user in e.Users)
            {
                (long? EffectiveLimit, int Count) result; long desired;
                result = tracker.Remove(user.Id, key);
                desired = 0;
                await coordinator.RunAsync(user.Id, () => users.SetAsync(user.Id, desired = Min(tracker.GetEffectiveLimit(user.Id), pending.GetEffectiveLimit(user.Id), GetDefault()))).ConfigureAwait(false);
                logger.LogInformation("Playback stopped. User={UserId} Session={SessionId} Item={ItemId} RemainingStreams={RemainingStreams} EffectiveUserLimit={EffectiveLimit}", user.Id, e.Session.Id, e.Item.Id, result.Count, desired);
            }
        }
        catch (Exception ex) { logger.LogError(ex, "Failed to process playback stop"); }
    }

    private async Task HandleSessionEndAsync(string sessionId)
    {
        foreach (var userId in tracker.RemoveSession(sessionId))
        {
            await coordinator.RunAsync(userId, () => users.SetAsync(userId, Min(tracker.GetEffectiveLimit(userId), pending.GetEffectiveLimit(userId), GetDefault()))).ConfigureAwait(false);
        }
    }

    private Task SetDefaultAsync(Guid userId) => users.SetAsync(userId, GetDefault());
    private long GetDefault() => BitratePolicyService.ToBitsPerSecond(Math.Max(0.1m, Plugin.Instance?.Configuration.StandardBitrateMbps ?? 20));
    private static string GetKey(string? playSessionId, string sessionId, Guid itemId) => string.IsNullOrWhiteSpace(playSessionId) ? $"{sessionId}:{itemId:N}" : playSessionId;
    private static long Min(long? active, long? reserved, long fallback) => new[] { active, reserved }.Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty(fallback).Min();

    private async Task CleanupPendingAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var userId in pending.RemoveExpired(DateTimeOffset.UtcNow))
                {
                    await coordinator.RunAsync(userId, () => users.SetAsync(userId, Min(tracker.GetEffectiveLimit(userId), pending.GetEffectiveLimit(userId), GetDefault()))).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task<(int? Width, int? Height)> GetSourceDimensionsAsync(PlaybackProgressEventArgs e, string key)
    {
        if (_sourceDimensions.TryGetValue(key, out var cached)) return cached;
        var sourceList = await mediaSources.GetPlaybackMediaSources(e.Item!, e.Users[0], true, true, CancellationToken.None).ConfigureAwait(false);
        var selected = sourceList.FirstOrDefault(x => string.Equals(x.Id, e.MediaSourceId, StringComparison.OrdinalIgnoreCase)) ?? sourceList.FirstOrDefault();
        var video = selected?.MediaStreams.FirstOrDefault(x => x.Type == MediaBrowser.Model.Entities.MediaStreamType.Video);
        var result = (video?.Width ?? e.MediaInfo?.Width, video?.Height ?? e.MediaInfo?.Height);
        _sourceDimensions[key] = result;
        return result;
    }
}
