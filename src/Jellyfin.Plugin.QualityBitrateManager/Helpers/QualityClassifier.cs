using Jellyfin.Plugin.QualityBitrateManager.Models;

namespace Jellyfin.Plugin.QualityBitrateManager.Helpers;

public static class QualityClassifier
{
    // The larger dimension helps ultrawide/cropped sources remain in their intended class.
    public static QualityTier? Classify(int? width, int? height)
    {
        if (width is null or <= 0 || height is null or <= 0) return null;
        var referenceHeight = Math.Max(height.Value, (int)Math.Round(width.Value * 9d / 16d));
        return referenceHeight switch
        {
            > 1440 => QualityTier.P2160,
            > 1080 => QualityTier.P1440,
            > 720 => QualityTier.P1080,
            > 480 => QualityTier.P720,
            _ => QualityTier.P480
        };
    }
}
