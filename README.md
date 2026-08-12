# Quality Bitrate Manager for Jellyfin

Quality Bitrate Manager applies configurable remote bitrate limits according to the original resolution of a video. It supports 2160p/4K, 1440p, 1080p, 720p and 480p, decimal Mbit/s values, concurrent streams, and an integrated bandwidth assistant.

The configuration page automatically uses German when Jellyfin's interface language is German; otherwise it uses English.

## Plugin repository

Add the following repository URL in **Jellyfin Dashboard → Plugins → Repositories**:

```text
https://raw.githubusercontent.com/philipkraeutl/jellyfin-plugin-quality-bitrate-manager/main/manifest.json
```

Direct link: [Quality Bitrate Manager manifest](https://raw.githubusercontent.com/philipkraeutl/jellyfin-plugin-quality-bitrate-manager/main/manifest.json)

After saving the repository, open the Jellyfin plugin catalog, select **Quality Bitrate Manager**, install it and restart Jellyfin.

## Important disclaimer

> [!WARNING]
> This plugin **modifies and persists Jellyfin's per-user `RemoteClientBitrateLimit` property**. It does not create a separate session-only limit. Existing remote bitrate limits configured for users can therefore be overwritten.

By installing and enabling the plugin, you should understand the following behavior:

- A user's remote bitrate limit is changed when that user requests playback; installing or starting the plugin does not rewrite every user pre-emptively.
- Before a playback request is evaluated, the affected user's limit is changed to the matching quality limit.
- After the user's last tracked stream ends, the limit is restored to the plugin's configured default—not necessarily to the value that existed before installing the plugin.
- Stopping or disabling the plugin restores tracked users to the configured default.
- Unclean client disconnects are handled through session-end events and pending-request expiry where Jellyfin supplies those signals, but no event-based system can guarantee recovery from every crash or abrupt process termination.
- Administrators should record existing user limits before installing the plugin and use a test server first.

The plugin does not modify Jellyfin Core and does not use Reflection, Harmony, binary patching or similar runtime modifications.

## Compatibility

- Jellyfin Server **10.11.x**
- .NET 9
- Plugin version **1.0.0.0**

The release binary is compiled against the oldest supported 10.11 API and CI also verifies it against Jellyfin 10.11.11. Its manifest therefore declares `targetAbi` 10.11.0.0.

Jellyfin 10.10.x is not included in this manifest entry. Although the shared source currently compiles against Jellyfin 10.10.7, that server generation uses .NET 8 and needs a separately packaged and runtime-tested binary with its own manifest version entry. Do not install the 10.11 ZIP on Jellyfin 10.10.

## Installation

### From a release ZIP

1. Download `quality-bitrate-manager_1.0.0.0.zip` from the GitHub release.
2. Stop Jellyfin.
3. Create a plugin directory named `Quality Bitrate Manager` inside Jellyfin's plugin directory.
4. Extract the ZIP into that directory.
5. Start Jellyfin.
6. Open Dashboard → Plugins → Quality Bitrate Manager.

Common plugin directories:

- Windows service: `C:\ProgramData\Jellyfin\Server\plugins`
- Windows user installation: `%LOCALAPPDATA%\jellyfin\plugins`
- Linux packages: `/var/lib/jellyfin/plugins`
- Docker: `/config/plugins`

Always back up Jellyfin's configuration before installing a third-party plugin.

### Build from source

```powershell
dotnet restore QualityBitrateManager.slnx
dotnet test QualityBitrateManager.slnx
dotnet publish src/Jellyfin.Plugin.QualityBitrateManager -c Release
```

The published plugin is written to `src/Jellyfin.Plugin.QualityBitrateManager/bin/Release/net9.0/publish`.

### Automated releases

Every push and pull request runs the build and test workflow. Successful CI runs also provide the compiled plugin DLL as a temporary workflow artifact.

The release workflow can be started in either of two ways:

1. Push a version tag such as `v1.0.0.0`.
2. Open **Actions → Release plugin → Run workflow** and enter `1.0.0.0`.

Before publishing, the workflow verifies that the requested version matches `build.yaml` and all project version fields. It then runs all tests, creates the plugin ZIP and a `.sha256` checksum, updates `manifest.json` with Jellyfin's required MD5 checksum and download URL, and creates a GitHub release with generated release notes. Finally, it commits the updated manifest to the default branch so Jellyfin can discover the new version.

For a normal release, update these files first:

- all three version fields in `src/Jellyfin.Plugin.QualityBitrateManager/Jellyfin.Plugin.QualityBitrateManager.csproj`
- `build.yaml`
- `CHANGELOG.md`
- `manifest.json` when compatibility or plugin metadata changes; version, download URL, checksum and timestamp are updated automatically
- the compatibility and version information in this README

Then commit and push the changes before creating the tag. The workflow uses GitHub's automatically provided `GITHUB_TOKEN`; no personal access token or signing secret is required.

## Configuration

The plugin adds its own entry to the Plugins section of Jellyfin's dashboard.

- **Default bitrate limit:** used whenever no enabled quality rule matches and after the last tracked stream ends.
- **2160p / 4K / UHD**
- **1440p / QHD**
- **1080p / Full HD**
- **720p / HD**
- **480p / SD**

All quality rules are disabled by default. Values are entered in Mbit/s and converted using `1 Mbit/s = 1,000,000 bit/s`.

### Bandwidth assistant

The assistant accepts:

- router/internet upload rate;
- percentage reserved for Jellyfin;
- expected concurrent stream count per quality tier;
- an optional hardware-transcoding profile;
- optional fixed AV1/HEVC minimum values for maximum concurrency.

The output is guidance, not a guarantee. Codec, source complexity, HDR, audio, subtitles, client capabilities and hardware encoder quality all affect the required bitrate. If an expected 4K stream is calculated below 6.5 Mbit/s, the assistant warns that Jellyfin or the client may choose a 1080p transcode instead.

## How it works

Jellyfin reads `RemoteClientBitrateLimit` while `MediaInfoHelper.SetDeviceSpecificData` builds the playback decision and transcoding URL. A normal `PlaybackStart` event occurs too late for the initial stream decision.

Quality Bitrate Manager therefore registers a standard ASP.NET Core `IAsyncActionFilter` for Jellyfin's `POST /Items/{itemId}/PlaybackInfo` request. Before Jellyfin's controller selects a stream, the filter:

1. identifies the authenticated user and requested item;
2. reads the original selected media source resolution;
3. classifies the quality tier;
4. persists the selected value in the user's `RemoteClientBitrateLimit`;
5. allows Jellyfin to continue building the playback response.

The request is temporarily tracked as pending. `PlaybackStart` converts it into an active playback; `PlaybackStopped`, `SessionEnded` and a pending-request timeout clean it up.

### Concurrent streams

`RemoteClientBitrateLimit` belongs to a user, not to an individual session. One user cannot have separate limits for two simultaneous streams. If the same user has multiple active or pending streams, the plugin applies the **lowest active limit**.

Example:

```text
4K stream:    6.5 Mbit/s
1080p stream: 3.5 Mbit/s
Effective user limit: 3.5 Mbit/s
```

This conservative behavior prevents the lower-bandwidth stream from being assigned the higher limit, but it can also constrain the other stream.

### Resolution classification

The classifier uses the original media source dimensions. To handle cropped and ultrawide content, its reference height is the greater of the actual height and `width × 9/16`.

| Reference height | Tier |
|---:|---|
| > 1440 | 2160p / 4K |
| 1081–1440 | 1440p |
| 721–1080 | 1080p |
| 481–720 | 720p |
| ≤ 480 | 480p |

Audio-only playback is ignored. Live TV is supported when Jellyfin supplies usable video dimensions.

## Known limitations

- The early hook is coupled to Jellyfin's PlaybackInfo route and ASP.NET Core controller pipeline. Future Jellyfin releases may require an update.
- A lower `MaxStreamingBitrate` supplied by the client still takes precedence.
- Bitrate limits affect remote clients according to Jellyfin's own local-network detection.
- Changing a user property is inherently global to that user and may affect playback requests made by other devices at the same time.
- The plugin cannot preserve an arbitrary pre-installation limit because its configured default is the explicit restoration target.
- Very low bitrate values can cause Jellyfin to select a lower output resolution even when the original source is 4K.

Source references: [MediaInfoHelper 10.11.11](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Jellyfin.Api/Helpers/MediaInfoHelper.cs), [MediaInfoController 10.11.11](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Jellyfin.Api/Controllers/MediaInfoController.cs), [SessionManager 10.11.11](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Emby.Server.Implementations/Session/SessionManager.cs).

## Development and tests

```powershell
dotnet test QualityBitrateManager.slnx
./scripts/package.ps1
```

Unit tests cover resolution classification, policy fallback, decimal conversion, duplicate events, concurrent tracker operations and pending-playback expiry.

## License

Jellyfin server libraries are GPL-licensed. Distribute compiled builds of this plugin under GPL-compatible terms and include the applicable license information with releases.
