[CmdletBinding()]
param(
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$project = Join-Path $root "Jellyfin.Plugin.AzureIllusion\Jellyfin.Plugin.AzureIllusion.csproj"
$solution = Join-Path $root "AzureIllusion.Plugin.sln"
$projectXml = [xml](Get-Content -LiteralPath $project -Raw -Encoding UTF8)
$version = [string]$projectXml.Project.PropertyGroup.Version
if (-not $version) {
    throw "Nie znaleziono numeru wersji w pliku projektu."
}

$buildYaml = Get-Content -LiteralPath (Join-Path $root "build.yaml") -Raw -Encoding UTF8
$yamlVersionMatch = [regex]::Match($buildYaml, '(?m)^version:\s*"(?<value>[^"]+)"')
$targetAbiMatch = [regex]::Match($buildYaml, '(?m)^targetAbi:\s*"(?<value>[^"]+)"')
if (-not $yamlVersionMatch.Success -or $yamlVersionMatch.Groups['value'].Value -ne $version) {
    throw "Wersja w build.yaml musi byc zgodna z wersja $version z pliku projektu."
}

if (-not $targetAbiMatch.Success) {
    throw "Nie znaleziono targetAbi w build.yaml."
}

$targetAbi = $targetAbiMatch.Groups['value'].Value
$output = Join-Path $root "Jellyfin.Plugin.AzureIllusion\bin\Release\net9.0"
$artifacts = Join-Path $root "artifacts"
$stage = Join-Path $artifacts "AzureIllusion"
$archive = Join-Path $artifacts "AzureIllusion_$version.zip"
$sha256Path = "$archive.sha256"
$md5Path = "$archive.md5"
$metadataPath = Join-Path $artifacts "release-metadata.json"

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnet = if ($dotnetCommand) {
    $dotnetCommand.Source
} elseif ($env:DOTNET_ROOT -and (Test-Path -LiteralPath (Join-Path $env:DOTNET_ROOT "dotnet.exe"))) {
    Join-Path $env:DOTNET_ROOT "dotnet.exe"
} elseif ($env:ProgramFiles -and (Test-Path -LiteralPath (Join-Path $env:ProgramFiles "dotnet\dotnet.exe"))) {
    Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
} else {
    throw "Nie znaleziono zestawu .NET SDK. Zainstaluj .NET 9 SDK albo ustaw zmienna DOTNET_ROOT."
}

& $dotnet restore $solution --nologo --verbosity minimal -m:1 /nodeReuse:false
if ($LASTEXITCODE -ne 0) {
    throw "Przywracanie zaleznosci wtyczki nie powiodlo sie."
}

if (-not $SkipTests) {
    & $dotnet test $solution -c Release --no-restore --nologo --verbosity minimal -m:1 /nodeReuse:false
    if ($LASTEXITCODE -ne 0) {
        throw "Testy wtyczki nie powiodly sie."
    }
}

& $dotnet build $project -c Release --no-restore --nologo --verbosity minimal -m:1 /nodeReuse:false
if ($LASTEXITCODE -ne 0) {
    throw "Budowanie wtyczki nie powiodlo sie."
}

if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}

New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $output "Jellyfin.Plugin.AzureIllusion.dll") -Destination $stage
Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination $stage
Copy-Item -LiteralPath (Join-Path $root "LICENSE.md") -Destination $stage
Copy-Item -LiteralPath (Join-Path $root "build.yaml") -Destination $stage

if (Test-Path -LiteralPath $archive) {
    Remove-Item -LiteralPath $archive -Force
}

Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $archive -CompressionLevel Optimal
$sha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
$md5 = (Get-FileHash -LiteralPath $archive -Algorithm MD5).Hash.ToLowerInvariant()
Set-Content -LiteralPath $sha256Path -Value "$sha256  AzureIllusion_$version.zip" -Encoding ascii
Set-Content -LiteralPath $md5Path -Value "$md5  AzureIllusion_$version.zip" -Encoding ascii

$metadata = [ordered]@{
    version = $version
    targetAbi = $targetAbi
    archiveName = "AzureIllusion_$version.zip"
    md5 = $md5
    sha256 = $sha256
    timestamp = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
}
$metadataJson = $metadata | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText($metadataPath, $metadataJson + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))

Write-Host "Utworzono: $archive"
Write-Host "MD5 dla katalogu Jellyfin: $md5"
Write-Host "SHA-256: $sha256"
Write-Host "Metadane wydania: $metadataPath"
