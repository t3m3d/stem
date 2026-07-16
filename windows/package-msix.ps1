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
$displayName = "Stem: Terminal for Windows"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $PSScriptRoot "Stem.Windows\Stem.Windows.csproj"
$manifestTemplate = Join-Path $PSScriptRoot "packaging\AppxManifest.xml"
$dist = [IO.Path]::GetFullPath((Join-Path $root "dist"))
$workRoot = [IO.Path]::GetFullPath((Join-Path $dist "msix-work"))
$layout = [IO.Path]::GetFullPath((Join-Path $workRoot "layout"))
$validation = [IO.Path]::GetFullPath((Join-Path $workRoot "validation"))
$artifact = Join-Path $dist "stem-$Version-x64.msix"
$symbolArtifact = Join-Path $dist "stem-$Version-x64.appxsym"
$uploadArtifact = Join-Path $dist "stem-$Version-x64.msixupload"

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
    "-p:DebugType=portable",
    "-p:DebugSymbols=true",
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
$manifest.Package.Properties.DisplayName = $displayName
$manifest.Package.Applications.Application.VisualElements.DisplayName = $displayName
$manifest.Save((Join-Path $layout "AppxManifest.xml"))

Add-Type -AssemblyName System.Drawing
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
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit

        $size = [Math]::Max(1, [single]([Math]::Min($Width, $Height) * $Scale))
        $x = [single](($Width - $size) / 2)
        $y = [single](($Height - $size) / 2)
        $radius = [single]($size * 0.22)
        $diameter = [single]($radius * 2)
        $rect = [Drawing.RectangleF]::new($x, $y, $size, $size)
        $shape = [Drawing.Drawing2D.GraphicsPath]::new()
        $shape.AddArc($x, $y, $diameter, $diameter, 180, 90)
        $shape.AddArc($x + $size - $diameter, $y, $diameter, $diameter, 270, 90)
        $shape.AddArc($x + $size - $diameter, $y + $size - $diameter, $diameter, $diameter, 0, 90)
        $shape.AddArc($x, $y + $size - $diameter, $diameter, $diameter, 90, 90)
        $shape.CloseFigure()

        $fill = [Drawing.Drawing2D.LinearGradientBrush]::new(
            $rect,
            [Drawing.ColorTranslator]::FromHtml("#7C3AED"),
            [Drawing.ColorTranslator]::FromHtml("#2E1065"),
            45.0)
        $outline = [Drawing.Pen]::new(
            [Drawing.ColorTranslator]::FromHtml("#D7C3FF"),
            [single][Math]::Max(1, $size * 0.025))
        $font = [Drawing.Font]::new(
            "Segoe UI",
            [single]($size * 0.48),
            [Drawing.FontStyle]::Bold,
            [Drawing.GraphicsUnit]::Pixel)
        $format = [Drawing.StringFormat]::new()
        $format.Alignment = [Drawing.StringAlignment]::Center
        $format.LineAlignment = [Drawing.StringAlignment]::Center
        $letter = [Drawing.SolidBrush]::new([Drawing.Color]::White)
        try {
            $graphics.FillPath($fill, $shape)
            $graphics.DrawPath($outline, $shape)
            $graphics.DrawString("K", $font, $letter, $rect, $format)
        }
        finally {
            $letter.Dispose()
            $format.Dispose()
            $font.Dispose()
            $outline.Dispose()
            $fill.Dispose()
            $shape.Dispose()
        }
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

$symbolFiles = @(Get-ChildItem -LiteralPath $layout -Filter *.pdb -Recurse)
if ($symbolFiles.Count -eq 0) {
    throw "The Store publish did not produce portable symbols."
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$symbolStage = Join-Path $workRoot "symbols"
New-Item -ItemType Directory -Path $symbolStage -Force | Out-Null
foreach ($symbol in $symbolFiles) {
    Copy-Item -LiteralPath $symbol.FullName -Destination (Join-Path $symbolStage $symbol.Name) -Force
    Remove-Item -LiteralPath $symbol.FullName -Force
}
if (Test-Path -LiteralPath $symbolArtifact) {
    Remove-Item -LiteralPath $symbolArtifact -Force
}
[IO.Compression.ZipFile]::CreateFromDirectory(
    $symbolStage,
    $symbolArtifact,
    [IO.Compression.CompressionLevel]::Optimal,
    $false)

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
        $signature = Get-AuthenticodeSignature -LiteralPath $artifact
        $expectedStoreCertificateWarning =
            $null -ne $signature.SignerCertificate -and
            $signature.SignatureType -eq [System.Management.Automation.SignatureType]::Authenticode -and
            $signature.SignerCertificate.Subject -eq $publisher -and
            $signature.SignerCertificate.Thumbprint -eq $signingCertificate.Thumbprint -and
            $signature.Status -eq [System.Management.Automation.SignatureStatus]::UnknownError -and
            $signature.StatusMessage -match "root certificate which is not trusted"
        if (!$expectedStoreCertificateWarning) {
            throw "The MSIX signature did not verify."
        }
        Write-Warning "The package signature is intact and the publisher matches, but the self-signed Store certificate is not rooted locally. Microsoft Store ingestion will re-sign the package."
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
    $packedManifest.Package.Properties.DisplayName -ne $displayName -or
    $packedManifest.Package.Properties.PublisherDisplayName -ne $publisherDisplayName) {
    throw "The packed manifest identity does not match the Microsoft Store reservation."
}

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $artifact).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$artifact.sha256" -Encoding ascii -Value "$hash  $([IO.Path]::GetFileName($artifact))"

$uploadStage = Join-Path $workRoot "upload"
New-Item -ItemType Directory -Path $uploadStage -Force | Out-Null
Copy-Item -LiteralPath $artifact -Destination $uploadStage -Force
Copy-Item -LiteralPath $symbolArtifact -Destination $uploadStage -Force
if (Test-Path -LiteralPath $uploadArtifact) {
    Remove-Item -LiteralPath $uploadArtifact -Force
}
[IO.Compression.ZipFile]::CreateFromDirectory(
    $uploadStage,
    $uploadArtifact,
    [IO.Compression.CompressionLevel]::Optimal,
    $false)
$uploadHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $uploadArtifact).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$uploadArtifact.sha256" -Encoding ascii -Value "$uploadHash  $([IO.Path]::GetFileName($uploadArtifact))"

Write-Host "MSIX $artifact"
Write-Host "Identity $identityName"
Write-Host "Publisher $publisher"
Write-Host "PublisherDisplayName $publisherDisplayName"
Write-Host "DisplayName $displayName"
Write-Host "Version $Version"
Write-Host "Signed $(!$Unsigned)"
if ($null -ne $signingCertificate) {
    Write-Host "Certificate $($signingCertificate.Thumbprint)"
}
Write-Host "SHA256 $hash"
Write-Host "MSIXUPLOAD $uploadArtifact"
Write-Host "MSIXUPLOAD-SHA256 $uploadHash"
Write-Host "Symbols $symbolArtifact"
