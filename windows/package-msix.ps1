[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version = "0.1.0.0",
    [ValidateSet("win-x64")]
    [string]$Runtime = "win-x64",
    [string]$CertificateThumbprint = "",
    [switch]$Unsigned
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$identityName = "t3m3d.StemTerminalforWindows"
$publisher = "CN=97613709-C254-4F66-AB6B-1EE4BA3D003F"
$publisherDisplayName = "t3m3d"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $PSScriptRoot "Stem.Windows\Stem.Windows.csproj"
$manifestTemplate = Join-Path $PSScriptRoot "packaging\AppxManifest.xml"
$sourceIcon = Join-Path $PSScriptRoot "Stem.Windows\stem.ico"
$dist = [IO.Path]::GetFullPath((Join-Path $root "dist"))
$workRoot = [IO.Path]::GetFullPath((Join-Path $dist "msix-work"))
$layout = [IO.Path]::GetFullPath((Join-Path $workRoot "layout"))
$validation = [IO.Path]::GetFullPath((Join-Path $workRoot "validation"))
$artifact = Join-Path $dist "stem-$Version-x64.msix"

foreach ($part in $Version.Split('.')) {
    if ([int]$part -gt 65535) {
        throw "MSIX version components must be between 0 and 65535."
    }
}

foreach ($path in @($workRoot, $layout, $validation)) {
    if (!$path.StartsWith($dist + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to use a packaging path outside $dist"
    }
}

$kitsRoot = Join-Path ([Environment]::GetFolderPath("ProgramFilesX86")) "Windows Kits\10\bin"
$makeAppx = Get-ChildItem $kitsRoot -Filter makeappx.exe -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Directory.Name -eq "x64" -and $_.Directory.Parent.Name -match '^10\.0\.\d+\.\d+$' } |
    Sort-Object { [version]$_.Directory.Parent.Name } -Descending |
    Select-Object -First 1
if ($null -eq $makeAppx) {
    throw "makeappx.exe was not found. Install the Windows 10/11 SDK."
}
$signTool = Join-Path $makeAppx.Directory.FullName "signtool.exe"
if (!$Unsigned -and !(Test-Path -LiteralPath $signTool)) {
    throw "signtool.exe was not found beside $($makeAppx.FullName)."
}

& (Join-Path $PSScriptRoot "build.ps1") -Configuration Release -Test
if ($LASTEXITCODE -ne 0) {
    throw "STEM Windows release tests failed."
}

if (Test-Path -LiteralPath $workRoot) {
    Remove-Item -LiteralPath $workRoot -Recurse -Force
}
New-Item -ItemType Directory -Path (Join-Path $layout "Assets") -Force | Out-Null
New-Item -ItemType Directory -Path $dist -Force | Out-Null

$assemblyVersion = [string]::Join(".", $Version.Split('.')[0..2])
$publishArguments = @(
    "publish",
    $project,
    "--configuration", "Release",
    "--runtime", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:Version=$assemblyVersion",
    "--output", $layout
)
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "STEM Windows publish failed."
}

[xml]$manifest = Get-Content -LiteralPath $manifestTemplate -Raw
$manifest.Package.Identity.Version = $Version
$manifest.Package.Identity.Name = $identityName
$manifest.Package.Identity.Publisher = $publisher
$manifest.Package.Properties.PublisherDisplayName = $publisherDisplayName
$manifest.Save((Join-Path $layout "AppxManifest.xml"))

Add-Type -AssemblyName System.Drawing
$source = [Drawing.Icon]::new($sourceIcon).ToBitmap()
try {
    function New-StemLogo {
        param(
            [Parameter(Mandatory)] [string]$Path,
            [Parameter(Mandatory)] [int]$Width,
            [Parameter(Mandatory)] [int]$Height,
            [double]$Scale = 0.78
        )

        $canvas = [Drawing.Bitmap]::new($Width, $Height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($canvas)
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $size = [Math]::Max(1, [int]([Math]::Min($Width, $Height) * $Scale))
            $x = [int](($Width - $size) / 2)
            $y = [int](($Height - $size) / 2)
            $graphics.DrawImage($source, $x, $y, $size, $size)
            $canvas.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $graphics.Dispose()
            $canvas.Dispose()
        }
    }

    $assets = Join-Path $layout "Assets"
    New-StemLogo (Join-Path $assets "StoreLogo.png") 50 50 0.80
    New-StemLogo (Join-Path $assets "Square44x44Logo.png") 44 44 0.74
    New-StemLogo (Join-Path $assets "Square150x150Logo.png") 150 150 0.78
    New-StemLogo (Join-Path $assets "Square310x310Logo.png") 310 310 0.66
    New-StemLogo (Join-Path $assets "Wide310x150Logo.png") 310 150 0.72
    New-StemLogo (Join-Path $assets "SplashScreen.png") 620 300 0.62
}
finally {
    $source.Dispose()
}

Copy-Item (Join-Path $root "LICENSE") $layout -Force
Copy-Item (Join-Path $PSScriptRoot "README.md") (Join-Path $layout "WINDOWS-README.md") -Force
Copy-Item (Join-Path $root "stem.conf") (Join-Path $layout "stem.conf.example") -Force

if (Test-Path -LiteralPath $artifact) {
    Remove-Item -LiteralPath $artifact -Force
}
& $makeAppx.FullName pack /d $layout /p $artifact /o
if ($LASTEXITCODE -ne 0) {
    throw "makeappx failed to create the MSIX."
}

$signingCertificate = $null
if (!$Unsigned) {
    if (![string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        $signingCertificate = Get-Item "Cert:\CurrentUser\My\$CertificateThumbprint" -ErrorAction Stop
    }
    else {
        $signingCertificate = Get-ChildItem Cert:\CurrentUser\My |
            Where-Object {
                $_.Subject -eq $publisher -and
                $_.HasPrivateKey -and
                $_.NotBefore -le (Get-Date) -and
                $_.NotAfter -gt (Get-Date) -and
                ($_.EnhancedKeyUsageList.ObjectId -contains "1.3.6.1.5.5.7.3.3")
            } |
            Sort-Object NotAfter -Descending |
            Select-Object -First 1
    }
    if ($null -eq $signingCertificate) {
        throw "No valid CurrentUser code-signing certificate matches $publisher. Use -Unsigned for a Store-ingestion-only package."
    }

    & $signTool sign /fd SHA256 /sha1 $signingCertificate.Thumbprint /s My $artifact
    if ($LASTEXITCODE -ne 0) {
        throw "signtool failed to sign the MSIX."
    }
    & $signTool verify /pa /v $artifact
    if ($LASTEXITCODE -ne 0) {
        throw "The MSIX signature did not verify."
    }
}

if (Test-Path -LiteralPath $validation) {
    Remove-Item -LiteralPath $validation -Recurse -Force
}
& $makeAppx.FullName unpack /p $artifact /d $validation /o
if ($LASTEXITCODE -ne 0) {
    throw "makeappx could not validate/unpack the produced MSIX."
}
[xml]$packedManifest = Get-Content -LiteralPath (Join-Path $validation "AppxManifest.xml") -Raw
if ($packedManifest.Package.Identity.Name -ne $identityName -or
    $packedManifest.Package.Identity.Publisher -ne $publisher -or
    $packedManifest.Package.Identity.Version -ne $Version -or
    $packedManifest.Package.Properties.PublisherDisplayName -ne $publisherDisplayName) {
    throw "The packed manifest identity does not match the Microsoft Store reservation."
}

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $artifact).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$artifact.sha256" -Encoding ascii -Value "$hash  $([IO.Path]::GetFileName($artifact))"

Write-Host "MSIX $artifact"
Write-Host "Identity $identityName"
Write-Host "Publisher $publisher"
Write-Host "PublisherDisplayName $publisherDisplayName"
Write-Host "Version $Version"
Write-Host "Signed $(!$Unsigned)"
if ($null -ne $signingCertificate) {
    Write-Host "Certificate $($signingCertificate.Thumbprint)"
}
Write-Host "SHA256 $hash"
