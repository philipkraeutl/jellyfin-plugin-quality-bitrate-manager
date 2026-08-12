using Jellyfin.Plugin.QualityBitrateManager.Configuration;
using Jellyfin.Plugin.QualityBitrateManager.Models;

namespace Jellyfin.Plugin.QualityBitrateManager.Services;

public sealed class BitratePolicyService
{
    public const long BitsPerMegabit = 1_000_000;
    public static long ToBitsPerSecond(decimal mbps) => checked((long)decimal.Round(mbps * BitsPerMegabit, 0, MidpointRounding.AwayFromZero));

    public long GetLimit(QualityTier? tier, PluginConfiguration config)
    {
        var mbps = tier switch
        {
            QualityTier.P2160 when config.Enable2160p => config.Bitrate2160pMbps,
            QualityTier.P1440 when config.Enable1440p => config.Bitrate1440pMbps,
            QualityTier.P1080 when config.Enable1080p => config.Bitrate1080pMbps,
            QualityTier.P720 when config.Enable720p => config.Bitrate720pMbps,
            QualityTier.P480 when config.Enable480p => config.Bitrate480pMbps,
            _ => config.StandardBitrateMbps
        };
        return ToBitsPerSecond(Math.Max(0.1m, mbps));
    }
}
