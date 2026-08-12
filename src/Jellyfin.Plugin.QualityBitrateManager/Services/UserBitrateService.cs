using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.QualityBitrateManager.Services;

public sealed class UserBitrateService(IUserManager userManager, ILogger<UserBitrateService> logger)
{
    public async Task SetAsync(Guid userId, long desiredLimit)
    {
        var user = userManager.GetUserById(userId);
        if (user is null) { logger.LogWarning("Cannot update bitrate: user {UserId} no longer exists", userId); return; }
        if (user.RemoteClientBitrateLimit == desiredLimit) return;
        user.RemoteClientBitrateLimit = checked((int)desiredLimit);
        await userManager.UpdateUserAsync(user).ConfigureAwait(false);
        logger.LogInformation("Updated RemoteClientBitrateLimit. User={UserId} Limit={Limit}", userId, desiredLimit);
    }
}
