[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_.-]+$')]
    [string]$GitHubOwner,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_.-]+$')]
    [string]$GitHubRepository,

    [string]$ExistingManifest,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "catalog-output")
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$config = Get-Content -LiteralPath (Join-Path $root "release-config.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$metadata = Get-Content -LiteralPath (Join-Path $root "artifacts\release-metadata.json") -Raw -Encoding UTF8 | ConvertFrom-Json

if ($metadata.md5 -notmatch '^[a-f0-9]{32}$') {
    throw "Metadane wydania nie zawieraja prawidlowej sumy MD5. Najpierw uruchom build-plugin.ps1."
}

$versions = @()
if ($ExistingManifest -and (Test-Path -LiteralPath $ExistingManifest)) {
    $existingText = Get-Content -LiteralPath $ExistingManifest -Raw -Encoding UTF8
    if ($existingText.Trim()) {
        $existingCatalog = @($existingText | ConvertFrom-Json)
        $existingPlugin = $existingCatalog | Where-Object { $_.guid -eq $config.guid } | Select-Object -First 1
        if ($existingPlugin) {
            $versions = @($existingPlugin.versions | Where-Object { $_.version -ne $metadata.version })
        }
    }
}

$sourceUrl = "https://github.com/$GitHubOwner/$GitHubRepository/releases/download/v$($metadata.version)/$($metadata.archiveName)"
$currentVersion = [ordered]@{
    checksum = $metadata.md5
    changelog = $config.changelog
    targetAbi = $metadata.targetAbi
    sourceUrl = $sourceUrl
    timestamp = $metadata.timestamp
    version = $metadata.version
}

$plugin = [ordered]@{
    category = $config.category
    guid = $config.guid
    name = $config.name
    description = $config.description
    owner = $config.owner
    overview = $config.overview
    versions = @($currentVersion) + $versions
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$manifestPath = Join-Path $OutputDirectory "manifest.json"
$repositoryUrlPath = Join-Path $OutputDirectory "repository-url.txt"
$manifestJson = @($plugin) | ConvertTo-Json -Depth 10
[IO.File]::WriteAllText($manifestPath, $manifestJson + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))

$repositoryUrl = "https://raw.githubusercontent.com/$GitHubOwner/$GitHubRepository/catalog/manifest.json"
[IO.File]::WriteAllText($repositoryUrlPath, $repositoryUrl + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))

Write-Host "Manifest: $manifestPath"
Write-Host "Adres repozytorium Jellyfin: $repositoryUrl"
