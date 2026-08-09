[CmdletBinding()]
param(
    [string] $CacheRoot = (Join-Path $PSScriptRoot '..\artifacts\prerequisites'),
    [switch] $SkipDownload
)

$ErrorActionPreference = 'Stop'

$presentMonVersion = '2.5.1'
$msiFileName = "PresentMon-v$presentMonVersion.msi"
$downloadUri = "https://github.com/GameTechDev/PresentMon/releases/download/v$presentMonVersion/$msiFileName"
$expectedSha256 = '4DDE95A71DAA44BA9379C60C8686404DD4EDC8F567EFCCBB2A69BEA2CFB9A694'
if (Test-Path -LiteralPath $CacheRoot) {
    $resolvedCacheRoot = (Resolve-Path -LiteralPath $CacheRoot).Path
}
else {
    $resolvedCacheRoot = $CacheRoot
}

$cacheDirectory = Join-Path $resolvedCacheRoot 'PresentMon'
$msiPath = Join-Path $cacheDirectory $msiFileName

function Test-PinnedPresentMonMsi {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $false
    }

    $actualSha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if (-not [string]::Equals($actualSha256, $expectedSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The cached PresentMon MSI does not match the pinned v$presentMonVersion SHA-256. Expected $expectedSha256, got ${actualSha256}: $Path"
    }

    return $true
}

if (Test-PinnedPresentMonMsi -Path $msiPath) {
    Write-Output $msiPath
    return
}

if ($SkipDownload) {
    throw "Pinned PresentMon v$presentMonVersion MSI is not cached at '$msiPath'. Run this script without -SkipDownload."
}

New-Item -ItemType Directory -Path $cacheDirectory -Force | Out-Null
$temporaryPath = "$msiPath.download"
try {
    Invoke-WebRequest -Uri $downloadUri -OutFile $temporaryPath
    $actualSha256 = (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash
    if (-not [string]::Equals($actualSha256, $expectedSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Downloaded PresentMon MSI failed pinned SHA-256 verification. Expected $expectedSha256, got $actualSha256."
    }

    Move-Item -LiteralPath $temporaryPath -Destination $msiPath -Force
}
finally {
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}

if (-not (Test-PinnedPresentMonMsi -Path $msiPath)) {
    throw "Pinned PresentMon v$presentMonVersion MSI was not created at '$msiPath'."
}

Write-Output $msiPath
