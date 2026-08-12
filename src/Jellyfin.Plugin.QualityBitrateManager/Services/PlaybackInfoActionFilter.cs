using System.Globalization;
using Jellyfin.Plugin.QualityBitrateManager.Helpers;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.QualityBitrateManager.Services;

public sealed class PlaybackInfoActionFilter(
    ILibraryManager library,
    IMediaSourceManager mediaSources,
    IUserManager userManager,
    BitratePolicyService policy,
    ActivePlaybackTracker active,
    PendingPlaybackTracker pending,
    UserOperationCoordinator coordinator,
    UserBitrateService users,
    ILogger<PlaybackInfoActionFilter> logger) : IAsyncActionFilter
{
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(2);

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var itemId = Guid.Empty;
        var userId = Guid.Empty;
        if (!HttpMethods.IsPost(context.HttpContext.Request.Method)
            || context.HttpContext.Request.Path.Value?.EndsWith("/PlaybackInfo", StringComparison.OrdinalIgnoreCase) != true
            || !TryGetGuid(context.RouteData.Values["itemId"], out itemId)
            || !TryGetUserId(context, out userId))
        {
            await next().ConfigureAwait(false);
            return;
        }

        PendingPlayback? reservation = null;
        try
        {
            var user = userManager.GetUserById(userId);
            var item = user is null ? null : library.GetItemById<MediaBrowser.Controller.Entities.BaseItem>(itemId, user);
            if (user is not null && item?.MediaType == Jellyfin.Data.Enums.MediaType.Video && Plugin.Instance?.Configuration is { } config)
            {
                var sources = await mediaSources.GetPlaybackMediaSources(item, user, true, true, CancellationToken.None).ConfigureAwait(false);
                var video = sources.SelectMany(x => x.MediaStreams)
                    .Where(x => x.Type == MediaBrowser.Model.Entities.MediaStreamType.Video)
                    .OrderByDescending(x => (long)(x.Width ?? 0) * (x.Height ?? 0))
                    .FirstOrDefault();
                var quality = QualityClassifier.Classify(video?.Width, video?.Height);
                var limit = policy.GetLimit(quality, config);
                reservation = pending.Add(userId, itemId, limit, PendingLifetime);
                await coordinator.RunAsync(userId, () => users.SetAsync(userId, Min(active.GetEffectiveLimit(userId), pending.GetEffectiveLimit(userId), DefaultLimit(config)))).ConfigureAwait(false);
                logger.LogInformation("PlaybackInfo intercepted before stream selection. User={UserId} Item={ItemId} SourceResolution={Width}x{Height} Quality={Quality} ReservedLimit={Limit}", userId, itemId, video?.Width, video?.Height, quality, limit);
            }

        }
        catch (Exception ex)
        {
            if (reservation is not null) pending.Remove(userId, reservation.Key);
            logger.LogError(ex, "Failed to prepare bitrate before PlaybackInfo request. Item={ItemId}", itemId);
            var config = Plugin.Instance?.Configuration;
            if (config is not null && userId != Guid.Empty)
                await coordinator.RunAsync(userId, () => users.SetAsync(userId, Min(active.GetEffectiveLimit(userId), pending.GetEffectiveLimit(userId), DefaultLimit(config)))).ConfigureAwait(false);
        }

        var executed = await next().ConfigureAwait(false);
        if (reservation is not null && (executed.Exception is not null || executed.HttpContext.Response.StatusCode >= 400))
        {
            pending.Remove(userId, reservation.Key);
            var config = Plugin.Instance?.Configuration;
            if (config is not null)
                await coordinator.RunAsync(userId, () => users.SetAsync(userId, Min(active.GetEffectiveLimit(userId), pending.GetEffectiveLimit(userId), DefaultLimit(config)))).ConfigureAwait(false);
        }
    }

    private static long Min(long? active, long? pending, long fallback) => new[] { active, pending }.Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty(fallback).Min();
    private static long DefaultLimit(Configuration.PluginConfiguration config) => BitratePolicyService.ToBitsPerSecond(Math.Max(0.1m, config.StandardBitrateMbps));
    private static bool TryGetGuid(object? value, out Guid result) => Guid.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out result);
    private static bool TryGetUserId(ActionExecutingContext context, out Guid userId)
    {
        var claim = context.HttpContext.User.Claims.FirstOrDefault(x => string.Equals(x.Type, "Jellyfin-UserId", StringComparison.OrdinalIgnoreCase));
        return Guid.TryParse(claim?.Value, out userId);
    }
}
