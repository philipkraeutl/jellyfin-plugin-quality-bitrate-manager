param(
    [string]$Version = "1.0.0.0",
    [string]$Configuration = "Release",
    [string]$Framework = "net9.0",
    [string]$JellyfinVersion = "10.11.0",
    [string]$PackageSuffix = "",
    [string]$DotnetCommand = "dotnet"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src/Jellyfin.Plugin.QualityBitrateManager/Jellyfin.Plugin.QualityBitrateManager.csproj"
$publish = Join-Path $root "src/Jellyfin.Plugin.QualityBitrateManager/bin/$Configuration/$Framework/publish"
$artifacts = Join-Path $root "artifacts"
$archive = Join-Path $artifacts "quality-bitrate-manager$PackageSuffix`_$Version.zip"
$checksum = "$archive.sha256"

& $DotnetCommand publish $project -c $Configuration -f $Framework -p:JellyfinTargetFramework=$Framework -p:JellyfinVersion=$JellyfinVersion -p:PluginVersion=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive }
if (Test-Path -LiteralPath $checksum) { Remove-Item -LiteralPath $checksum }
Compress-Archive -LiteralPath (Join-Path $publish "Jellyfin.Plugin.QualityBitrateManager.dll") -DestinationPath $archive

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash.ToLowerInvariant()
$archiveName = Split-Path -Leaf $archive
Set-Content -LiteralPath $checksum -Value "$hash  $archiveName" -Encoding Ascii
Write-Host "Created: $archive"
Write-Host "Created: $checksum"
Write-Host "SHA256:  $hash"
