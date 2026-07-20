[CmdletBinding()]
param(
    [string]$ManifestPath = (Join-Path $PSScriptRoot "catalog-output\manifest.json")
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$metadata = Get-Content -LiteralPath (Join-Path $root "artifacts\release-metadata.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$manifestText = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8
if (-not $manifestText.TrimStart().StartsWith("[")) {
    throw "Katalog Jellyfin musi byc tablica JSON na najwyzszym poziomie."
}

$catalog = @($manifestText | ConvertFrom-Json)

if ($catalog.Count -ne 1) {
    throw "Katalog musi zawierac dokladnie jedna definicje wtyczki."
}

$version = @($catalog[0].versions)[0]
if ($version.version -ne $metadata.version -or $version.targetAbi -ne $metadata.targetAbi) {
    throw "Wersja lub targetAbi w manifeście nie zgadza sie z paczka."
}

$archive = Join-Path $root "artifacts\$($metadata.archiveName)"
if (-not (Test-Path -LiteralPath $archive)) {
    throw "Brakuje paczki $archive."
}

$actualMd5 = (Get-FileHash -LiteralPath $archive -Algorithm MD5).Hash.ToLowerInvariant()
if ($version.checksum -ne $actualMd5) {
    throw "Suma MD5 w manifeście nie zgadza sie z paczka."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $required = @("Jellyfin.Plugin.AzureIllusion.dll", "build.yaml", "README.md", "LICENSE.md")
    $entryNames = @($zip.Entries | ForEach-Object { $_.FullName })
    foreach ($name in $required) {
        if ($entryNames -notcontains $name) {
            throw "W paczce brakuje pliku $name."
        }
    }
} finally {
    $zip.Dispose()
}

if ($version.sourceUrl -notmatch '^https://github\.com/[^/]+/[^/]+/releases/download/v') {
    throw "sourceUrl nie wskazuje na wydanie GitHub."
}

Write-Host "Katalog, suma MD5 i zawartosc ZIP sa prawidlowe."
