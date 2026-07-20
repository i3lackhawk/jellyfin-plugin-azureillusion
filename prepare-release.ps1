[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_.-]+$')]
    [string]$GitHubOwner,

    [ValidatePattern('^[A-Za-z0-9_.-]+$')]
    [string]$GitHubRepository = "jellyfin-plugin-azureillusion",

    [string]$ExistingManifest
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

& (Join-Path $root "build-plugin.ps1")

$manifestArguments = @{
    GitHubOwner = $GitHubOwner
    GitHubRepository = $GitHubRepository
}
if ($ExistingManifest) {
    $manifestArguments.ExistingManifest = $ExistingManifest
}

& (Join-Path $root "update-manifest.ps1") @manifestArguments
& (Join-Path $root "test-repository.ps1")

$metadata = Get-Content -LiteralPath (Join-Path $root "artifacts\release-metadata.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$repositoryUrl = Get-Content -LiteralPath (Join-Path $root "catalog-output\repository-url.txt") -Raw -Encoding UTF8

Write-Host ""
Write-Host "Wydanie $($metadata.version) jest gotowe do opublikowania."
Write-Host "Tag GitHub: v$($metadata.version)"
Write-Host "Paczka: artifacts\$($metadata.archiveName)"
Write-Host "Adres katalogu Jellyfin po publikacji: $($repositoryUrl.Trim())"

