[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$Test
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$app = Join-Path $root "windows\Stem.Windows\Stem.Windows.csproj"
$smoke = Join-Path $root "windows\Stem.Windows.Smoke\Stem.Windows.Smoke.csproj"
$target = if ($Test) { $smoke } else { $app }

Push-Location $root
try {
    dotnet build $target --configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "STEM Windows build failed."
    }

    if ($Test) {
        dotnet run --project $smoke --configuration $Configuration --no-build --no-restore
        if ($LASTEXITCODE -ne 0) {
            throw "STEM Windows smoke test failed."
        }
    }
}
finally {
    Pop-Location
}

