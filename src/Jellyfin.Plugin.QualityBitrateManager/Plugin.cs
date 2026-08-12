using Jellyfin.Plugin.QualityBitrateManager.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.QualityBitrateManager;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin? Instance { get; private set; }
    public Plugin(IApplicationPaths paths, IXmlSerializer serializer) : base(paths, serializer) => Instance = this;
    public override string Name => "Quality Bitrate Manager";
    public override Guid Id => Guid.Parse("fb632c57-b35d-4a47-a91d-f7088f0cb15e");
    public IEnumerable<PluginPageInfo> GetPages() =>
    [
        new()
        {
            Name = "qualityBitrateManager",
            DisplayName = "Quality Bitrate Manager",
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.config.html",
            EnableInMainMenu = true,
            MenuSection = "plugins",
            MenuIcon = "speed"
        }
    ];
}
