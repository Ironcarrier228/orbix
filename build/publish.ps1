<#
.SYNOPSIS
    Builds the distributable files of Orbix (run it from any directory, needs the .NET 8 SDK).

.DESCRIPTION
    portable              self-contained single-file Orbix.exe: no prerequisites, ~60 MB (compressed).
    framework-dependent   single-file Orbix.exe of a few MB, needs the ".NET 8 Desktop Runtime" on the PC.
    all                   both of them.

    Output: artifacts/<variant>/Orbix.exe (next to the repository root unless -OutputRoot is given).
    With -Installer the Inno Setup compiler (ISCC.exe) additionally builds artifacts/installer/Orbix-Setup-<version>.exe
    from the portable build (Inno Setup 6: https://jrsoftware.org/isinfo.php).

.EXAMPLE
    ./build/publish.ps1                        # portable Orbix.exe -> artifacts/portable
    ./build/publish.ps1 -Variant all -Installer -Version 1.2.0
#>
[CmdletBinding()]
param(
    [ValidateSet('portable', 'framework-dependent', 'all')]
    [string]$Variant = 'portable',

    [string]$Runtime = 'win-x64',

    [string]$Version = '',

    [string]$OutputRoot = '',

    [switch]$Installer
)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutputRoot) { $OutputRoot = Join-Path $repo 'artifacts' }
$project = Join-Path $repo 'src/Orbix/Orbix.csproj'

function Publish-Variant {
    param([string]$Name, [bool]$SelfContained)

    $out = Join-Path $OutputRoot $Name
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }

    $dotnetArgs = @(
        'publish', $project,
        '-c', 'Release',
        '-r', $Runtime,
        '--self-contained', $SelfContained.ToString().ToLowerInvariant(),
        '-p:PublishSingleFile=true',
        '-p:PublishReadyToRun=true',     # pre-compiled code: the menu opens fast even on the very first use
        '-p:DebugType=none',
        '-p:DebugSymbols=false',
        '-o', $out,
        '--nologo', '-v', 'minimal'
    )
    if ($SelfContained) {
        $dotnetArgs += '-p:IncludeNativeLibrariesForSelfExtract=true'   # WPF native DLLs live inside the exe
        $dotnetArgs += '-p:EnableCompressionInSingleFile=true'
    }
    if ($Version) { $dotnetArgs += "-p:Version=$Version" }

    Write-Host "== dotnet publish ($Name) =="
    & dotnet @dotnetArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for '$Name' (exit code $LASTEXITCODE)" }

    # single-file output must be just the exe (plus symbols, which are disabled)
    Get-ChildItem $out -Filter '*.pdb' -ErrorAction SilentlyContinue | Remove-Item -Force
    $exe = Join-Path $out 'Orbix.exe'
    if (-not (Test-Path $exe)) { throw "Orbix.exe was not produced in $out" }

    $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "OK: $exe ($size MB)"
    return $exe
}

$built = @{}
if ($Variant -in 'portable', 'all') { $built['portable'] = Publish-Variant 'portable' $true }
if ($Variant -in 'framework-dependent', 'all') { $built['framework-dependent'] = Publish-Variant 'framework-dependent' $false }

if ($Installer) {
    if (-not $built.ContainsKey('portable')) { $built['portable'] = Publish-Variant 'portable' $true }

    $iscc = (Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue)?.Source
    if (-not $iscc) {
        $candidates = @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
            "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe")
        $iscc = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    }
    if (-not $iscc) { throw 'ISCC.exe (Inno Setup 6) was not found. Install it from https://jrsoftware.org/isdl.php' }

    $effectiveVersion = if ($Version) { $Version } else { '1.0.0' }
    $installerOut = Join-Path $OutputRoot 'installer'
    New-Item -ItemType Directory -Force $installerOut | Out-Null

    Write-Host '== Inno Setup =='
    & $iscc "/DAppVersion=$effectiveVersion" "/DSourceExe=$($built['portable'])" "/O$installerOut" (Join-Path $repo 'build/installer.iss')
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed (exit code $LASTEXITCODE)" }
    Get-ChildItem $installerOut -Filter '*.exe' | ForEach-Object { Write-Host "OK: $($_.FullName) ($([math]::Round($_.Length / 1MB, 1)) MB)" }
}
