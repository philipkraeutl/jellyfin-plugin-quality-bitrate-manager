using Jellyfin.Plugin.QualityBitrateManager.Services;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.QualityBitrateManager;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, MediaBrowser.Controller.IServerApplicationHost applicationHost)
    {
        services.AddSingleton<ActivePlaybackTracker>();
        services.AddSingleton<PendingPlaybackTracker>();
        services.AddSingleton<UserOperationCoordinator>();
        services.AddSingleton<BitratePolicyService>();
        services.AddSingleton<UserBitrateService>();
        services.AddHostedService<PlaybackMonitor>();
        services.Configure<MvcOptions>(options => options.Filters.Add<PlaybackInfoActionFilter>());
    }
}
