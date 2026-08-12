# Quality Bitrate Manager

Jellyfin-Plugin für qualitätsabhängige, benutzerbezogene Remote-Bitratenlimits. Es setzt Jellyfins vorhandenes `RemoteClientBitrateLimit`; Änderungen am Jellyfin-Core oder Runtime-Patches sind nicht erforderlich.

## Kompatibilität und Installation

- Zielversion: Jellyfin Server **10.11.11**
- Laufzeit: .NET 9

`dotnet publish src/Jellyfin.Plugin.QualityBitrateManager -c Release` ausführen und den Inhalt des Publish-Ordners in einen eigenen Unterordner des Jellyfin-Plugin-Verzeichnisses kopieren. Danach Jellyfin neu starten. Die Serverversion muss zu den referenzierten Jellyfin-Paketen passen.

## Konfiguration

Im Dashboard steht unter **Plugins** ein eigener Navigationseintrag **Quality Bitrate Manager** bereit. Dort befinden sich ein Standardlimit (20 Mbit/s) sowie zunächst deaktivierte Regeln für 2160p/4K (35), 1440p (20), 1080p (12), 720p (6) und 480p (3). Dezimalwerte mit Punkt oder Komma sind erlaubt; Werte werden zentral mit `1 Mbit/s = 1.000.000 bit/s` umgerechnet.

Die Klassifikation nutzt Höhe und Breite. Als Referenzhöhe gilt das Maximum aus tatsächlicher Höhe und `Breite × 9/16`; dadurch werden etwa 3840×1600 als 2160p, 1920×800 als 1080p und 1280×536 als 720p eingeordnet. Unbekannte Auflösungen fallen auf das Standardlimit zurück. Audio-Wiedergabe wird ignoriert; Live-TV wird verarbeitet, sofern Jellyfin Videoabmessungen meldet.

## Funktionsweise

Ein globaler ASP.NET-Core-Action-Filter erkennt Jellyfins `POST /Items/{itemId}/PlaybackInfo`, ermittelt die Originalauflösung und setzt das Limit, bevor Jellyfins Controller die Streamauswahl berechnet. Die Anfrage wird bis zu zwei Minuten als „pending“ reserviert und von `PlaybackStart` übernommen; abgebrochene Anfragen werden automatisch bereinigt. `PlaybackMonitor` hört zusätzlich auf `PlaybackStart`, `PlaybackProgress`, `PlaybackStopped` und `SessionEnded`. Die Auflösung wird anhand der Original-Medienquelle ermittelt, nicht anhand der eventuell bereits herunterskalierten Player-Ausgabe.

`RemoteClientBitrateLimit` ist eine User- und keine Session-Einstellung. Bei mehreren Streams desselben Benutzers verwendet Version 1 deshalb das niedrigste aktive Limit. Per-User-Synchronisation verhindert, dass parallele Start-/Stop-Ereignisse einen noch aktiven Stream übersehen. Doppelte Ereignisse werden über die `PlaySessionId` ersetzt bzw. ignoriert. Bei `SessionEnded`, Plugin-Stopp sowie Server-/Plugin-Start wird auf das Standardlimit zurückgesetzt; ein gelöschter Benutzer wird lediglich protokolliert.

## Event-Timing und früher Request-Hook

Die Implementierung wurde gegen den Jellyfin-10.11.11-Quellcode geprüft. `MediaInfoHelper.SetDeviceSpecificData` lädt den Benutzer und ruft `GetMaxBitrate` auf, bevor `StreamBuilder.GetOptimalVideoStream` die Playback-Entscheidung und Transcoding-URL erzeugt. Dort wird `user.RemoteClientBitrateLimit` gelesen. `PlaybackStart` entsteht dagegen erst in `SessionManager.OnPlaybackStart`, nachdem ein Client seinen `/Sessions/Playing`-Start gemeldet hat.

Jellyfin stellt keinen speziellen öffentlichen Plugin-Playback-Hook vor dieser Stelle bereit. Das Plugin registriert deshalb einen regulären ASP.NET-Core-`IAsyncActionFilter`. Dieser läuft vor `MediaInfoController.GetPostedPlaybackInfo`, setzt das User-Limit und lässt erst danach Jellyfins `MediaInfoHelper.SetDeviceSpecificData` die Streamentscheidung berechnen. So kann auch der initiale Transcode das Qualitätslimit verwenden. Der Filter ist kein Runtime-Patch und verwendet weder Reflection noch Harmony; er ist jedoch an Jellyfins PlaybackInfo-Route gekoppelt und muss bei zukünftigen Jellyfin-Routenänderungen überprüft werden. Ein niedrigeres vom Client geliefertes `MaxStreamingBitrate` hat weiterhin Vorrang.

Quellcodebelege: [MediaInfoHelper (10.11.11)](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Jellyfin.Api/Helpers/MediaInfoHelper.cs), [SessionManager (10.11.11)](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Emby.Server.Implementations/Session/SessionManager.cs), [offizielles Plugin-Template](https://github.com/jellyfin/jellyfin-plugin-template).

## Entwicklung

```powershell
dotnet restore QualityBitrateManager.slnx
dotnet test QualityBitrateManager.slnx
dotnet publish src/Jellyfin.Plugin.QualityBitrateManager -c Release
```

Die Tests decken Klassifikationsgrenzen, Regel-Fallback, Einheitenumrechnung, idempotente Starts/Stops, mehrere Limits und konkurrierende Tracker-Ereignisse ab.
