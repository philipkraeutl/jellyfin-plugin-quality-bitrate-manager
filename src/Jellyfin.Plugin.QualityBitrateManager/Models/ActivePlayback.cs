namespace Jellyfin.Plugin.QualityBitrateManager.Models;

public sealed record ActivePlayback(string PlaybackKey, string SessionId, Guid ItemId, QualityTier? Quality, long RequiredLimit, DateTimeOffset LastSeen);
