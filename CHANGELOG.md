# Changelog

## 1.0.0.1 - 2026-08-13

- Added a separately compiled Jellyfin 12.0 RC3 / .NET 10 variant.
- Added a unified release workflow that publishes Jellyfin 10.11 and 12 packages together.
- Added both server generations to the shared Jellyfin repository manifest.

## 1.0.0.0 - 2026-08-12

- Initial public release for Jellyfin 10.11.x.
- Early PlaybackInfo request hook so the initial stream selection sees the quality limit.
- Configurable default, 2160p, 1440p, 1080p, 720p and 480p limits.
- Decimal Mbit/s values and original-media-source resolution detection.
- Conservative lowest-limit handling for concurrent streams of one user.
- Pending playback, session-end and shutdown cleanup.
- German and English dashboard configuration UI.
- Integrated bandwidth assistant with per-tier concurrent stream planning.
- Optional hardware-transcoding and fixed AV1/HEVC minimum profiles.
