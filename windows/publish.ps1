[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')]
    [string]$Version = "0.1.0",
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $PSScriptRoot "Stem.Windows\Stem.Windows.csproj"
$dist = Join-Path $root "dist"
$packageName = "stem-$Version-$Runtime"
$stage = Join-Path $dist $packageName
$archive = Join-Path $dist "$packageName.zip"

& (Join-Path $PSScriptRoot "build.ps1") -Configuration Release -Test
if ($LASTEXITCODE -ne 0) { throw "STEM release tests failed." }

New-Item -ItemType Directory -Path $stage -Force | Out-Null
dotnet publish $project --configuration Release --runtime $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    --output $stage
if ($LASTEXITCODE -ne 0) { throw "STEM Windows publish failed." }

Copy-Item (Join-Path $root "LICENSE") $stage -Force
Copy-Item (Join-Path $PSScriptRoot "README.md") (Join-Path $stage "WINDOWS-README.md") -Force
Copy-Item (Join-Path $root "stem.conf") (Join-Path $stage "stem.conf.example") -Force

Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $archive -CompressionLevel Optimal -Force
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$archive.sha256" -Encoding ascii -Value "$hash  $([IO.Path]::GetFileName($archive))"

Write-Host "Published $archive"
Write-Host "SHA256 $hash"
