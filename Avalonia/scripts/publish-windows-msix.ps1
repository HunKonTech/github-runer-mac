param(
    [string]$Version = "",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$PackageName = "HunKonTech.GitRunnerManager",
    [string]$Publisher = "CN=GitRunnerManager Dev",
    [string]$DisplayName = "Git Runner Manager",
    [string]$PublisherDisplayName = "HunKonTech",
    [switch]$ForceNewCertificate
)

$ErrorActionPreference = "Stop"

function Resolve-Version([string]$RawVersion) {
    $source = if ([string]::IsNullOrWhiteSpace($RawVersion)) { $env:APP_VERSION } else { $RawVersion }
    if ([string]::IsNullOrWhiteSpace($source)) {
        $source = "1.0.0"
    }

    $clean = $source.Trim().TrimStart('v')
    if ($clean.Contains('-')) {
        $clean = $clean.Split('-')[0]
    }

    $parts = $clean.Split('.', [System.StringSplitOptions]::RemoveEmptyEntries)
    $numbers = @()
    foreach ($part in $parts) {
        $parsed = 0
        [void][int]::TryParse($part, [ref]$parsed)
        $numbers += $parsed
    }

    while ($numbers.Count -lt 4) {
        $numbers += 0
    }

    return ($numbers[0..3] -join '.')
}

function Resolve-AssetVersion([string]$RawVersion) {
    $source = if ([string]::IsNullOrWhiteSpace($RawVersion)) { $env:APP_VERSION } else { $RawVersion }
    if ([string]::IsNullOrWhiteSpace($source)) {
        return "1.0.0"
    }

    $clean = $source.Trim().TrimStart('v')
    if ($clean.Contains('-')) {
        $clean = $clean.Split('-')[0]
    }

    return $clean
}

function Find-WindowsSdkTool([string]$ToolName) {
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (-not (Test-Path $kitsRoot)) {
        throw "Windows SDK nem található: $kitsRoot"
    }

    $match = Get-ChildItem -Path $kitsRoot -Recurse -Filter $ToolName -File |
        Where-Object { $_.FullName -match "\\x64\\" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if ($null -eq $match) {
        throw "$ToolName nem található a Windows SDK bin könyvtáraiban."
    }

    return $match.FullName
}

function Get-OrCreateSigningCertificate([string]$Subject, [string]$CerPath, [switch]$ForceNew) {
    $existing = if (-not $ForceNew) {
        Get-ChildItem Cert:\CurrentUser\My |
            Where-Object { $_.Subject -eq $Subject -and $_.NotAfter -gt (Get-Date) } |
            Sort-Object NotAfter -Descending |
            Select-Object -First 1
    }

    if ($null -ne $existing) {
        Export-Certificate -Cert $existing -FilePath $CerPath -Force | Out-Null
        return $existing
    }

    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $Subject `
        -FriendlyName "GitRunnerManager MSIX Signing" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -KeyUsage DigitalSignature `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3")

    Export-Certificate -Cert $cert -FilePath $CerPath -Force | Out-Null
    return $cert
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$avaloniaDir = Resolve-Path (Join-Path $scriptDir "..")
$projectFile = Join-Path $avaloniaDir "src\GitRunnerManager.App\GitRunnerManager.App.csproj"
$manifestTemplate = Join-Path $avaloniaDir "packaging\msix\AppxManifest.template.xml"
$packageAsset = Join-Path $avaloniaDir "packaging\msix\Assets\AppIcon.png"
$readmeTemplate = Join-Path $avaloniaDir "packaging\msix\README_TEMPLATE.txt"

if (-not (Test-Path $projectFile)) {
    throw "Project file does not exist: $projectFile"
}

if (-not (Test-Path $manifestTemplate)) {
    throw "Manifest template does not exist: $manifestTemplate"
}

if (-not (Test-Path $packageAsset)) {
    throw "MSIX asset does not exist: $packageAsset"
}

$packageVersion = Resolve-Version $Version
$assetVersion = Resolve-AssetVersion $Version
$outputRoot = Join-Path $avaloniaDir "publish\msix\$RuntimeIdentifier"
$appPublishDir = Join-Path $outputRoot "app"
$layoutDir = Join-Path $outputRoot "layout"
$appLayoutDir = Join-Path $layoutDir "GitRunnerManager"
$assetsLayoutDir = Join-Path $layoutDir "Assets"
$packageDir = Join-Path $outputRoot "package"
$manifestPath = Join-Path $layoutDir "AppxManifest.xml"
$msixFileName = "GitRunnerManager-{0}-{1}.msix" -f $assetVersion, $RuntimeIdentifier
$zipFileName = "GitRunnerManager-{0}-{1}.zip" -f $assetVersion, $RuntimeIdentifier
$msixPath = Join-Path $packageDir $msixFileName
$readmePath = Join-Path $packageDir "readme.txt"
$cerPath = Join-Path $packageDir "GitRunnerManager-Sideload.cer"
$zipPath = Join-Path $outputRoot $zipFileName

$makeAppx = Find-WindowsSdkTool "makeappx.exe"
$signTool = Find-WindowsSdkTool "signtool.exe"

Remove-Item -LiteralPath $appPublishDir, $layoutDir, $packageDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $appPublishDir, $appLayoutDir, $assetsLayoutDir, $packageDir -Force | Out-Null

dotnet publish $projectFile `
    -c Release `
    -r $RuntimeIdentifier `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:Version=$packageVersion `
    -o $appPublishDir

Copy-Item -Path (Join-Path $appPublishDir "*") -Destination $appLayoutDir -Recurse -Force
Copy-Item -LiteralPath $packageAsset -Destination (Join-Path $assetsLayoutDir "AppIcon.png") -Force

$manifest = Get-Content $manifestTemplate -Raw
$manifest = $manifest.Replace("__PACKAGE_NAME__", $PackageName)
$manifest = $manifest.Replace("__PUBLISHER__", $Publisher)
$manifest = $manifest.Replace("__PACKAGE_VERSION__", $packageVersion)
$manifest = $manifest.Replace("__DISPLAY_NAME__", $DisplayName)
$manifest = $manifest.Replace("__PUBLISHER_DISPLAY_NAME__", $PublisherDisplayName)
[System.IO.File]::WriteAllText($manifestPath, $manifest, [System.Text.Encoding]::UTF8)

$certificate = Get-OrCreateSigningCertificate -Subject $Publisher -CerPath $cerPath -ForceNew:$ForceNewCertificate

if (Test-Path $msixPath) {
    Remove-Item -LiteralPath $msixPath -Force
}

& $makeAppx pack /d $layoutDir /p $msixPath /o | Out-Host
& $signTool sign /fd SHA256 /sha1 $certificate.Thumbprint /s My $msixPath | Out-Host

$readme = Get-Content $readmeTemplate -Raw
$readme = $readme.Replace("<verzio>", $assetVersion)
$readme = $readme.Replace("<runtime>", $RuntimeIdentifier)
$readme = $readme.Replace("<msix>", $msixFileName)
[System.IO.File]::WriteAllText($readmePath, $readme, [System.Text.Encoding]::UTF8)

Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipPath -Force

Write-Host "MSIX package created: $msixPath"
Write-Host "Certificate exported: $cerPath"
Write-Host "Readme created: $readmePath"
Write-Host "Release ZIP created: $zipPath"
