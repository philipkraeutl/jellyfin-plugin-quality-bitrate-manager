using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.QualityBitrateManager.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public decimal StandardBitrateMbps { get; set; } = 20;
    public bool Enable2160p { get; set; }
    public decimal Bitrate2160pMbps { get; set; } = 35;
    public bool Enable1440p { get; set; }
    public decimal Bitrate1440pMbps { get; set; } = 20;
    public bool Enable1080p { get; set; }
    public decimal Bitrate1080pMbps { get; set; } = 12;
    public bool Enable720p { get; set; }
    public decimal Bitrate720pMbps { get; set; } = 6;
    public bool Enable480p { get; set; }
    public decimal Bitrate480pMbps { get; set; } = 3;
}
