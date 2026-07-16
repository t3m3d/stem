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

        $appExe = Join-Path $root "windows\Stem.Windows\bin\$Configuration\net8.0-windows10.0.17763.0\stem.exe"
        $token = [guid]::NewGuid().ToString("N")
        $temp = [IO.Path]::GetTempPath()
        $smokeConfig = Join-Path $temp "stem-startup-$token.conf"
        $smokeSession = Join-Path $temp "stem-startup-$token.json"
        $smokeLog = Join-Path $temp "stem-startup-$token.log"
        $previousConf = [Environment]::GetEnvironmentVariable("STEM_CONF")
        $previousSession = [Environment]::GetEnvironmentVariable("STEM_SESSION")
        $previousSmokeLog = [Environment]::GetEnvironmentVariable("STEM_STARTUP_SMOKE_LOG")

        try {
            $env:STEM_CONF = $smokeConfig
            $env:STEM_SESSION = $smokeSession
            $env:STEM_STARTUP_SMOKE_LOG = $smokeLog
            $startup = Start-Process -FilePath $appExe -ArgumentList "--startup-smoke" -PassThru -Wait
            if ($startup.ExitCode -ne 0) {
                $detail = if (Test-Path -LiteralPath $smokeLog) {
                    Get-Content -LiteralPath $smokeLog -Raw
                } else {
                    "No startup diagnostic was produced."
                }
                throw "STEM Windows XAML startup smoke test failed.
$detail"
            }
            Write-Host "PASS: WPF XAML startup"
        }
        finally {
            [Environment]::SetEnvironmentVariable("STEM_CONF", $previousConf)
            [Environment]::SetEnvironmentVariable("STEM_SESSION", $previousSession)
            [Environment]::SetEnvironmentVariable("STEM_STARTUP_SMOKE_LOG", $previousSmokeLog)
            Remove-Item -LiteralPath $smokeConfig, $smokeSession, $smokeLog -Force -ErrorAction SilentlyContinue
        }
    }
}
finally {
    Pop-Location
}

