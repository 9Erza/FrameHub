[CmdletBinding()]
param(
    [switch] $SkipRestore,
    [switch] $SkipPublish,
    [string] $IsccPath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot 'FrameHub.slnx'
$projectPath = Join-Path $repositoryRoot 'FrameHub.App\FrameHub.App.csproj'
$installerScript = Join-Path $PSScriptRoot 'FrameHub.iss'
$versionFile = Join-Path $repositoryRoot 'version.txt'

function Get-FrameHubVersion {
    if (-not (Test-Path -LiteralPath $versionFile -PathType Leaf)) {
        throw "FrameHub version source is missing: '$versionFile'."
    }

    $version = (Get-Content -LiteralPath $versionFile -Raw).Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw "FrameHub version in '$versionFile' must use major.minor.patch format; found '$version'."
    }

    return $version
}

function Resolve-IsccPath {
    param([string] $ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (-not (Test-Path -LiteralPath $ExplicitPath -PathType Leaf)) {
            throw "The explicit -IsccPath does not exist: '$ExplicitPath'."
        }

        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $candidates = [System.Collections.Generic.List[string]]::new()
    foreach ($programFiles in @(${env:ProgramFiles(x86)}, $env:ProgramFiles) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique) {
        $candidates.Add((Join-Path $programFiles 'Inno Setup 6\ISCC.exe'))
    }

    $registryKeys = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'
    )
    foreach ($registryKey in $registryKeys) {
        $entry = Get-ItemProperty -LiteralPath $registryKey -ErrorAction SilentlyContinue
        if ($null -ne $entry -and -not [string]::IsNullOrWhiteSpace($entry.InstallLocation)) {
            $candidates.Add((Join-Path $entry.InstallLocation 'ISCC.exe'))
        }
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw 'Inno Setup 6 compiler (ISCC.exe) was not found. Supply -IsccPath, add it to PATH, or install Inno Setup 6 in a standard location.'
}

$frameHubVersion = Get-FrameHubVersion
Write-Host "Using FrameHub version: $frameHubVersion"

& (Join-Path $PSScriptRoot 'Prepare-PresentMonPrerequisite.ps1') | Out-Host

if (-not $SkipRestore) {
    & dotnet restore $solutionPath
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

    & dotnet restore $projectPath --runtime win-x64
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore for win-x64 failed.' }
}

if (-not $SkipPublish) {
    & dotnet publish $projectPath --configuration Release --runtime win-x64 --self-contained true --no-restore --output (Join-Path $repositoryRoot 'artifacts\publish\win-x64')
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
}

$isccPath = Resolve-IsccPath -ExplicitPath $IsccPath
Write-Host "Using Inno Setup compiler: $isccPath"

& $isccPath "/DMyAppVersion=$frameHubVersion" $installerScript
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
