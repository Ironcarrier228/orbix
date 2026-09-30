# End-to-end test of the published Orbix.exe on a real (interactive) Windows desktop.
#
# The test drives the application like a user does: synthesized keyboard and mouse input (SendInput), window hit-testing
# (WindowFromPoint), real screenshots (CopyFromScreen). Every result is collected into one annotation ("e2e-report"),
# the screenshots are saved as files in -Out and are shipped by ci/emit-shots.ps1.
#
#   ./ci/e2e.ps1 -Exe artifacts/portable/Orbix.exe
param(
    [Parameter(Mandatory)][string]$Exe,
    [string]$Out = (Join-Path $PWD 'artifacts/e2e')
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Annotate.ps1"
Add-Type -AssemblyName System.Drawing
$compErrors = $null
Add-Type -TypeDefinition (Get-Content (Join-Path $PSScriptRoot 'E2E.Native.cs') -Raw) -ErrorVariable compErrors
if (-not ('E2E.Native' -as [type])) {
    # a broken helper class must not look like sixty application failures
    Write-Annotation 'e2e-summary' ("FATAL: E2E.Native.cs did not compile: " + (($compErrors | ForEach-Object { $_.ToString() }) -join ' | '))
    exit 1
}

[void][E2E.Native]::SetProcessDPIAware()

$Exe = (Resolve-Path $Exe).Path
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path

$script:Clock = [Diagnostics.Stopwatch]::StartNew()
$script:Results = [System.Collections.Generic.List[object]]::new()
$script:Info = [System.Collections.Generic.List[string]]::new()
$script:LogTails = [System.Collections.Generic.List[string]]::new()
$script:Children = [System.Collections.Generic.List[object]]::new()

$VK = @{ Ctrl = 0x11; Alt = 0x12; Space = 0x20; Esc = 0x1B; E = 0x45; N = 0x4E; O = 0x4F }
$Hotkey = [int[]]@($VK.Ctrl, $VK.Alt, $VK.Space)

# ------------------------------------------------------------------------------------------------- helpers

function Add-Check([string]$Name, [bool]$Ok, [string]$Detail = '') {
    $script:Results.Add([pscustomobject]@{ Name = $Name; Ok = $Ok; Detail = $Detail })
    $mark = if ($Ok) { 'PASS' } else { 'FAIL' }
    Write-Host "[$mark] $Name $Detail"
}

function Add-Info([string]$Text) {
    $script:Info.Add($Text)
    Write-Host "[info] $Text"
}

function Wait-Until([scriptblock]$Condition, [int]$TimeoutMs = 3000, [int]$PollMs = 25) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.ElapsedMilliseconds -lt $TimeoutMs) {
        if (& $Condition) { return [int]$clock.ElapsedMilliseconds }
        Start-Sleep -Milliseconds $PollMs
    }
    return -1
}

function Read-Log($App) {
    $path = Join-Path $App.Dir 'orbix.log'
    if (-not (Test-Path $path)) { return @() }
    try {
        $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8)
        $text = $reader.ReadToEnd()
        $reader.Dispose()
        return @($text -split "`r?`n" | Where-Object { $_ })
    }
    catch { return @() }
}

function Get-LayoutLines($App) { return @(Read-Log $App | Where-Object { $_ -match ' DEBUG Layout ' }) }

function Get-Layout($App) {
    $line = Get-LayoutLines $App | Select-Object -Last 1
    if (-not $line) { return }
    $body = $line.Substring($line.IndexOf('Layout ') + 7)
    $rings = @()
    foreach ($part in ($body -split ' \| ')) {
        if ($part -match '^L(\d+) r=(\S+) s=(\S+) \[(.*)\]$') {
            $slots = @()
            foreach ($xy in ($Matches[4] -split ';')) {
                $p = $xy -split ','
                if ($p.Count -eq 2) {
                    $slots += [pscustomobject]@{
                        X = [double]::Parse($p[0], [Globalization.CultureInfo]::InvariantCulture)
                        Y = [double]::Parse($p[1], [Globalization.CultureInfo]::InvariantCulture)
                    }
                }
            }
            $rings += [pscustomobject]@{ Level = [int]$Matches[1]; Radius = [double]$Matches[2]; Size = [double]$Matches[3]; Slots = $slots }
        }
    }
    return $rings
}

# waits until the log contains a newer layout with at least $MinRings rings
function Wait-Layout($App, [int]$Before, [int]$MinRings = 1, [int]$TimeoutMs = 3000) {
    $ms = Wait-Until { (Get-LayoutLines $App).Count -gt $Before -and @(Get-Layout $App).Count -ge $MinRings } $TimeoutMs 40
    Start-Sleep -Milliseconds 450   # let the animation finish
    return $ms
}

function Get-OverlayRect($App) {
    $r = [E2E.Native]::GetRect($App.Hwnd)
    return [pscustomobject]@{
        L = $r.Left; T = $r.Top; R = $r.Right; B = $r.Bottom; W = $r.Width; H = $r.Height
        CX = [int][math]::Round(($r.Left + $r.Right) / 2.0); CY = [int][math]::Round(($r.Top + $r.Bottom) / 2.0)
    }
}

function Get-SlotPoint($App, $Layout, [int]$Ring, [int]$Slot) {
    $r = Get-OverlayRect $App
    $scale = [E2E.Native]::SystemDpi / 96.0
    $s = $Layout[$Ring].Slots[$Slot]
    return [pscustomobject]@{ X = [int][math]::Round($r.CX + $s.X * $scale); Y = [int][math]::Round($r.CY + $s.Y * $scale) }
}

function Test-MenuOpen($App) { return ((Get-OverlayRect $App).W -gt 120) -and [E2E.Native]::IsVisible($App.Hwnd) }
function Test-OrbVisible($App) { return [E2E.Native]::IsVisible($App.Hwnd) -and ((Get-OverlayRect $App).W -lt 120) }

function Wait-MenuOpen($App, [int]$TimeoutMs = 2500) { return Wait-Until { Test-MenuOpen $App } $TimeoutMs 20 }
function Wait-MenuClosed($App, [int]$TimeoutMs = 2500) { return Wait-Until { -not (Test-MenuOpen $App) } $TimeoutMs 20 }

# opens the menu with the hotkey and waits for the layout; returns the milliseconds until the window has grown
function Open-ByHotkey($App) {
    $before = (Get-LayoutLines $App).Count
    $ms = [E2E.Native]::MeasureHotkey($App.Hwnd, 120, 3000, $Hotkey)
    if ($ms -ge 0) { [void](Wait-Layout $App $before 1 3000) }
    return $ms
}

function Close-ByEsc($App) {
    for ($i = 0; $i -lt 4 -and (Test-MenuOpen $App); $i++) {
        [E2E.Native]::Chord($VK.Esc)
        [void](Wait-MenuClosed $App 900)
    }
    return -not (Test-MenuOpen $App)
}

function Start-App([string]$Name, [string]$ConfigJson = '', [string[]]$AppArgs = @()) {
    $dir = Join-Path $Out "data-$Name"
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Force $dir | Out-Null
    if ($ConfigJson) { [IO.File]::WriteAllText((Join-Path $dir 'config.json'), $ConfigJson, [Text.UTF8Encoding]::new($false)) }

    $psi = [Diagnostics.ProcessStartInfo]::new($Exe)
    $psi.UseShellExecute = $false
    $psi.WorkingDirectory = Split-Path $Exe
    $psi.Environment['ORBIX_DATA_DIR'] = $dir
    $psi.Environment['ORBIX_LOG'] = 'debug'
    foreach ($a in $AppArgs) { $psi.ArgumentList.Add($a) }

    $clock = [Diagnostics.Stopwatch]::StartNew()
    $proc = [Diagnostics.Process]::Start($psi)
    $app = [pscustomobject]@{ Name = $Name; Dir = $dir; Proc = $proc; Hwnd = [IntPtr]::Zero; StartMs = -1; LogPos = 0 }
    $null = Wait-Until {
        $h = [E2E.Native]::FindWindow($proc.Id, 'OrbixOverlay')
        if ($h -ne [IntPtr]::Zero -and [E2E.Native]::IsVisible($h)) { $app.Hwnd = $h; $true } else { $false }
    } 25000 50
    $app.StartMs = [int]$clock.ElapsedMilliseconds
    return $app
}

function Invoke-Orbix($App, [string[]]$OrbixArgs = @(), [int]$TimeoutMs = 8000) {
    $psi = [Diagnostics.ProcessStartInfo]::new($Exe)
    $psi.UseShellExecute = $false
    $psi.WorkingDirectory = Split-Path $Exe
    $psi.Environment['ORBIX_DATA_DIR'] = $App.Dir
    foreach ($a in $OrbixArgs) { $psi.ArgumentList.Add($a) }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $p = [Diagnostics.Process]::Start($psi)
    $exited = $p.WaitForExit($TimeoutMs)
    $code = -1
    if ($exited) { $code = $p.ExitCode } else { try { $p.Kill() } catch { } }
    return [pscustomobject]@{ Exited = $exited; ExitCode = $code; Ms = [int]$clock.ElapsedMilliseconds }
}

function Stop-App($App) {
    if (-not $App -or $App.Proc.HasExited) { return $true }
    $r = Invoke-Orbix $App @('--exit') 8000
    $graceful = $App.Proc.WaitForExit(6000)
    if (-not $graceful) { try { $App.Proc.Kill() } catch { } }
    return $graceful
}

# app-side DEBUG traces (pointer hits, hover-open, clicks, visibility) collected since the previous call
function Add-LogTrace($App, [string]$Label) {
    $all = @(Read-Log $App)
    $new = @($all | Select-Object -Skip $App.LogPos | Where-Object { $_ -match ' DEBUG (Hot|HoverOpen|Expand|Click|Visibility|RefreshAll) ' })
    $App.LogPos = $all.Count
    $tail = @($new | Select-Object -Last 16 | ForEach-Object { if ($_.Length -gt 200) { $_.Substring(0, 200) } else { $_ } })
    Add-Info ("$Label trace:`n" + (($tail | ForEach-Object { $_ -replace '^\d{4}-\d\d-\d\d ', '' }) -join "`n"))
}

function Save-LogTail($App) {
    $lines = @(Read-Log $App | Where-Object { $_ -notmatch ' DEBUG Layout ' } | Select-Object -Last 26 | ForEach-Object { if ($_.Length -gt 230) { $_.Substring(0, 230) } else { $_ } })
    $script:LogTails.Add("--- $($App.Name) ---`n" + ($lines -join "`n"))
}

function Test-LogClean($App, [string]$Name) {
    $bad = @(Read-Log $App | Where-Object { $_ -match ' ERROR ' })
    Add-Check "$Name/log-has-no-errors" ($bad.Count -eq 0) (($bad | Select-Object -First 3) -join ' || ')
}

# screen region as a bitmap, layered windows included
function Get-ScreenBitmap([int]$X, [int]$Y, [int]$W, [int]$H) {
    $handle = [E2E.Native]::CaptureScreen($X, $Y, $W, $H)
    if ($handle -eq [IntPtr]::Zero) { throw "screen capture of $W x $H at $X,$Y failed" }
    try { return [System.Drawing.Image]::FromHbitmap($handle) }
    finally { [void][E2E.Native]::DeleteObject($handle) }
}

function Save-Shot([string]$Name, [int]$CX, [int]$CY, [int]$Half = 290, [string]$Ext = 'jpg') {
    $screenW = [E2E.Native]::ScreenWidth
    $screenH = [E2E.Native]::ScreenHeight
    $x = [math]::Max(0, $CX - $Half)
    $y = [math]::Max(0, $CY - $Half)
    $w = [math]::Min($screenW - $x, 2 * $Half)
    $h = [math]::Min($screenH - $y, 2 * $Half)
    $bmp = Get-ScreenBitmap $x $y $w $h
    $path = Join-Path $Out "$Name.$Ext"
    if ($Ext -eq 'png') {
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    else {
        $codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' } | Select-Object -First 1
        $params = [System.Drawing.Imaging.EncoderParameters]::new(1)
        $params.Param[0] = [System.Drawing.Imaging.EncoderParameter]::new([System.Drawing.Imaging.Encoder]::Quality, [long]88)
        $bmp.Save($path, $codec, $params)
    }
    $bmp.Dispose()
    Write-Host "[shot] $Name.$Ext ${w}x${h} at $x,$y ($([int]((Get-Item $path).Length / 1024)) KB)"
}

function Get-RegionMean([int]$CX, [int]$CY, [int]$Half = 8) {
    $size = 2 * $Half
    $bmp = Get-ScreenBitmap ($CX - $Half) ($CY - $Half) $size $size
    $r = 0.0; $gr = 0.0; $b = 0.0
    for ($y = 0; $y -lt $size; $y++) {
        for ($x = 0; $x -lt $size; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $r += $c.R; $gr += $c.G; $b += $c.B
        }
    }
    $bmp.Dispose()
    $n = [double]($size * $size)
    return [pscustomobject]@{ R = $r / $n; G = $gr / $n; B = $b / $n }
}

function Get-ColorDistance($A, $B) {
    return [math]::Sqrt([math]::Pow($A.R - $B.R, 2) + [math]::Pow($A.G - $B.G, 2) + [math]::Pow($A.B - $B.B, 2))
}

function Measure-Idle($App, [string]$Label, [int]$Seconds = 8) {
    $p = $App.Proc
    $p.Refresh()
    $cpu0 = $p.TotalProcessorTime.TotalMilliseconds
    $clock = [Diagnostics.Stopwatch]::StartNew()
    Start-Sleep -Seconds $Seconds
    $p.Refresh()
    $cpu1 = $p.TotalProcessorTime.TotalMilliseconds
    $perCore = ($cpu1 - $cpu0) / $clock.ElapsedMilliseconds * 100.0
    Add-Info ("idle[$Label]: CPU {0:N2}% of one core over {1}s, working set {2:N1} MB, private {3:N1} MB, threads {4}, handles {5}" -f `
            $perCore, $Seconds, ($p.WorkingSet64 / 1MB), ($p.PrivateMemorySize64 / 1MB), $p.Threads.Count, $p.HandleCount)
    return $perCore
}

function Start-Child([string]$Mode) {
    $child = Start-Process -FilePath 'pwsh' -ArgumentList @('-NoProfile', '-NonInteractive', '-File', (Join-Path $PSScriptRoot 'e2e-window.ps1'), '-Mode', $Mode) -PassThru -WindowStyle Hidden
    $script:Children.Add($child)
    $title = "OrbixE2E-$Mode"

    # find the helper window: by title first, then by scanning the child process (the form can appear before it is shown)
    $hwnd = [IntPtr]::Zero
    $null = Wait-Until {
        $h = [E2E.Native]::FindWindow(0, $title)
        if ($h -eq [IntPtr]::Zero) {
            foreach ($w in [E2E.Native]::WindowHandles($child.Id)) {
                if ([E2E.Native]::TitleOf($w) -eq $title) { $h = $w; break }
            }
        }
        if ($h -ne [IntPtr]::Zero) { $script:__childH = $h; $true } else { $false }
    } 20000 100
    $hwnd = $script:__childH
    $script:__childH = [IntPtr]::Zero

    if ($hwnd -ne [IntPtr]::Zero) {
        # WinForms sometimes leaves the form hidden in the CI session: show and place it ourselves
        [E2E.Native]::Show($hwnd)
        Start-Sleep -Milliseconds 300
        [E2E.Native]::Activate($hwnd)
        if ([E2E.Native]::GetRect($hwnd).Width -le 50) {
            $w = [E2E.Native]::GetSystemMetrics(0); $hgt = [E2E.Native]::GetSystemMetrics(1)
            [E2E.Native]::ForceRect($hwnd, [int](($w - 520) / 2), [int](($hgt - 340) / 2), 520, 340)
            Start-Sleep -Milliseconds 300
        }
    }
    return [pscustomobject]@{ Process = $child; Hwnd = $hwnd }
}

function Stop-Child($Child) {
    if ($Child -and $Child.Process -and -not $Child.Process.HasExited) { try { $Child.Process.Kill() } catch { } }
}

# ---- test data ---------------------------------------------------------------------------------------

function ItemDef([string]$Name, [string]$Kind, [string]$Target = '', [string]$Arguments = '', [string]$SystemAction = '') {
    $o = [ordered]@{ id = [guid]::NewGuid().ToString('N'); name = $Name; kind = $Kind }
    if ($Target) { $o['target'] = $Target }
    if ($Arguments) { $o['arguments'] = $Arguments }
    if ($SystemAction) { $o['systemAction'] = $SystemAction }
    return $o
}

function GroupDef([string]$Name, $Children) {
    return [ordered]@{ id = [guid]::NewGuid().ToString('N'); name = $Name; kind = 'group'; children = @($Children) }
}

function MakeConfig($Items, [hashtable]$Orb = @{}, [hashtable]$Menu = @{}, [hashtable]$Appearance = @{}, [hashtable]$General = @{}, [hashtable]$Triggers = @{}) {
    $orbD = [ordered]@{ size = 56; restingOpacity = 0.4; breathing = $true; visibility = 'always' }
    $menuD = [ordered]@{ radius = 128; itemSize = 52; submenuOpen = 'hover'; hoverDelayMs = 200 }
    $appD = [ordered]@{ theme = 'dark' }
    $genD = [ordered]@{ language = 'en'; welcomeShown = $true }
    $trgD = [ordered]@{ hotkeyEnabled = $true; hotkey = 'Ctrl+Alt+Space' }
    foreach ($k in $Orb.Keys) { $orbD[$k] = $Orb[$k] }
    foreach ($k in $Menu.Keys) { $menuD[$k] = $Menu[$k] }
    foreach ($k in $Appearance.Keys) { $appD[$k] = $Appearance[$k] }
    foreach ($k in $General.Keys) { $genD[$k] = $General[$k] }
    foreach ($k in $Triggers.Keys) { $trgD[$k] = $Triggers[$k] }
    $cfg = [ordered]@{
        version         = 1
        general         = $genD
        appearance      = $appD
        orb             = $orbD
        menu            = $menuD
        triggers        = $trgD
        activeProfileId = 'p1'
        profiles        = @([ordered]@{ id = 'p1'; name = 'E2E'; items = @($Items) })
    }
    return ($cfg | ConvertTo-Json -Depth 12)
}

function New-Wallpaper([string]$Path, [int]$W, [int]$H) {
    $bmp = [System.Drawing.Bitmap]::new($W, $H)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $rect = [System.Drawing.Rectangle]::new(0, 0, $W, $H)
    $bg = [System.Drawing.Drawing2D.LinearGradientBrush]::new($rect, [System.Drawing.Color]::FromArgb(255, 16, 20, 56), [System.Drawing.Color]::FromArgb(255, 12, 96, 120), 40.0)
    $g.FillRectangle($bg, $rect)
    $blobs = @(
        @(($W * 0.50), ($H * 0.50), 330, 255, 64, 160),
        @(($W * 0.12), ($H * 0.18), 300, 40, 140, 255),
        @(($W * 0.88), ($H * 0.25), 260, 255, 150, 40),
        @(($W * 0.80), ($H * 0.90), 320, 40, 220, 170),
        @(($W * 0.15), ($H * 0.85), 280, 140, 60, 255)
    )
    foreach ($b in $blobs) {
        $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $path.AddEllipse([single]($b[0] - $b[2]), [single]($b[1] - $b[2]), [single](2 * $b[2]), [single](2 * $b[2]))
        $pg = [System.Drawing.Drawing2D.PathGradientBrush]::new($path)
        $pg.CenterColor = [System.Drawing.Color]::FromArgb(210, [int]$b[3], [int]$b[4], [int]$b[5])
        $pg.SurroundColors = [System.Drawing.Color[]]@([System.Drawing.Color]::FromArgb(0, [int]$b[3], [int]$b[4], [int]$b[5]))
        $g.FillPath($pg, $path)
    }
    # fake windows and text: the frosted glass must have something to blur
    $pane = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(150, 245, 247, 250))
    $title = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(210, 30, 36, 52))
    $line = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(150, 30, 36, 52))
    $white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(120, 255, 255, 255))
    foreach ($p in @(@(40, 40, 300, 190), @(690, 60, 290, 200), @(60, 540, 330, 190), (@(650, 520, 320, 200)))) {
        $g.FillRectangle($pane, $p[0], $p[1], $p[2], $p[3])
        $g.FillRectangle($title, $p[0], $p[1], $p[2], 26)
        for ($i = 0; $i -lt 6; $i++) {
            $g.FillRectangle($line, $p[0] + 16, $p[1] + 44 + $i * 23, [int]($p[2] * (0.35 + 0.5 * ((($i * 37) % 10) / 10.0))), 9)
        }
    }
    for ($i = 0; $i -lt 14; $i++) {
        $g.FillRectangle($white, 200, 230 + $i * 22, [int](620 * (0.45 + 0.5 * ((($i * 53) % 10) / 10.0))), 8)
    }
    $font = [System.Drawing.Font]::new('Segoe UI', 72, [System.Drawing.FontStyle]::Bold)
    $g.DrawString('orbix', $font, [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(90, 255, 255, 255)), 330, 300)
    $g.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function Invoke-Scenario([string]$Name, [scriptblock]$Body) {
    Write-Host "=== scenario: $Name ==="
    try { & $Body }
    catch { Add-Check "$Name/exception" $false ("{0} (line {1})" -f $_.Exception.Message, $_.InvocationInfo.ScriptLineNumber) }
}

# ------------------------------------------------------------------------------------------------- environment

$screenW = [E2E.Native]::ScreenWidth
$screenH = [E2E.Native]::ScreenHeight
Add-Info ("screen {0}x{1}, system dpi {2}, session {3}, os {4}" -f $screenW, $screenH, [E2E.Native]::SystemDpi, [Diagnostics.Process]::GetCurrentProcess().SessionId, [Environment]::OSVersion.VersionString)
Add-Info ("exe: {0} ({1:N1} MB)" -f $Exe, ((Get-Item $Exe).Length / 1MB))

$wallpaper = Join-Path $Out 'wallpaper.png'
New-Wallpaper $wallpaper $screenW $screenH
Add-Check 'env/wallpaper' ([E2E.Native]::SetWallpaper($wallpaper)) ''
Start-Sleep -Milliseconds 1500
$minimized = [E2E.Native]::MinimizeAll([Diagnostics.Process]::GetCurrentProcess().Id)
Add-Info ("minimized windows: " + ($minimized -join ' ; '))
Start-Sleep -Milliseconds 800

$cx0 = [int]($screenW / 2)
$cy0 = [int]($screenH / 2)

# ------------------------------------------------------------------------------------------------- basic

Invoke-Scenario 'basic' {
    $markerDir = Join-Path $Out 'markers'
    New-Item -ItemType Directory -Force $markerDir | Out-Null
    $m1 = Join-Path $markerDir 'm1.txt'
    $m2 = Join-Path $markerDir 'm2.txt'
    Remove-Item $m1, $m2 -ErrorAction SilentlyContinue

    $items = @(
        (ItemDef 'Marker' 'app' 'cmd.exe' "/c echo launched>`"$m1`""),
        (GroupDef 'Folder' @(
            (GroupDef 'Sub' @(
                (ItemDef 'Deep' 'app' 'cmd.exe' "/c echo deep>`"$m2`""),
                (ItemDef 'Deep B' 'app' 'notepad.exe'),
                (ItemDef 'Deep C' 'url' 'https://example.com'))),
            (ItemDef 'Calc' 'app' 'calc.exe'),
            (ItemDef 'Paint' 'app' 'mspaint.exe'))),
        (ItemDef 'Missing' 'app' 'C:\orbix-e2e\missing\gone.exe'),
        (ItemDef 'Notepad' 'app' 'notepad.exe'),
        (ItemDef 'Web' 'url' 'https://example.com'),
        (ItemDef 'Shell' 'command' 'echo hello'),
        (ItemDef 'Lock' 'system' '' '' 'lock'),
        (ItemDef 'Calc' 'app' 'calc.exe')
    )
    $app = Start-App 'basic' (MakeConfig $items)
    try {
        $ok = $app.Hwnd -ne [IntPtr]::Zero
        Add-Check 'basic/window-appears' $ok ("start-to-window {0} ms" -f $app.StartMs)
        if (-not $ok) {
            Add-Info ("process alive={0}; windows: {1}" -f (-not $app.Proc.HasExited), ([E2E.Native]::ListWindows($app.Proc.Id) -join ' ; '))
            return
        }
        Start-Sleep -Milliseconds 1200

        # ---- the orb -------------------------------------------------------------------------------
        $r = Get-OverlayRect $app
        Add-Info ("orb window: {0},{1} {2}x{3}, centre {4},{5}; screen centre {6},{7}" -f $r.L, $r.T, $r.W, $r.H, $r.CX, $r.CY, $cx0, $cy0)
        Add-Check 'orb/centered' (([math]::Abs($r.CX - $cx0) -le 1) -and ([math]::Abs($r.CY - $cy0) -le 1)) "centre $($r.CX),$($r.CY) vs $cx0,$cy0"
        Add-Check 'orb/size-56' (([math]::Abs($r.W - 58) -le 3) -and ($r.W -eq $r.H)) "window $($r.W)x$($r.H)"

        $ex = [E2E.Native]::ExStyle($app.Hwnd)
        Add-Check 'orb/topmost' (($ex -band [E2E.Native]::WS_EX_TOPMOST) -ne 0) ('ex=0x{0:X}' -f $ex)
        Add-Check 'orb/not-in-alt-tab' ((($ex -band [E2E.Native]::WS_EX_TOOLWINDOW) -ne 0) -and (($ex -band [E2E.Native]::WS_EX_APPWINDOW) -eq 0)) ('ex=0x{0:X}' -f $ex)
        Add-Check 'orb/layered' (($ex -band [E2E.Native]::WS_EX_LAYERED) -ne 0) ('ex=0x{0:X}' -f $ex)
        Add-Check 'orb/no-activate' (($ex -band [E2E.Native]::WS_EX_NOACTIVATE) -ne 0) ('ex=0x{0:X}' -f $ex)

        $centerHit = [E2E.Native]::RootWindowAt($r.CX, $r.CY)
        Add-Check 'orb/hit-centre' ($centerHit -eq $app.Hwnd) ([E2E.Native]::DescribeWindow($centerHit))
        $insideHit = [E2E.Native]::RootWindowAt($r.CX + 22, $r.CY)
        Add-Check 'orb/hit-inside-circle' ($insideHit -eq $app.Hwnd) ([E2E.Native]::DescribeWindow($insideHit))
        $corners = @(@(($r.L + 1), ($r.T + 1)), @(($r.R - 2), ($r.T + 1)), @(($r.L + 1), ($r.B - 2)), @(($r.R - 2), ($r.B - 2)))
        $leaks = @()
        foreach ($c in $corners) {
            $h = [E2E.Native]::RootWindowAt($c[0], $c[1])
            if ($h -eq $app.Hwnd) { $leaks += "$($c[0]),$($c[1])" }
        }
        Add-Check 'orb/click-through-corners' ($leaks.Count -eq 0) ("leaking points: " + ($leaks -join ' '))
        $rimHit = [E2E.Native]::RootWindowAt($r.CX + 25, $r.CY + 25)
        Add-Check 'orb/click-through-outside-circle' ($rimHit -ne $app.Hwnd) ([E2E.Native]::DescribeWindow($rimHit))

        # ---- opacity: hidden orb (wallpaper), idle orb, hovered orb -------------------------------------
        [E2E.Native]::MoveTo(120, 120)
        $toggle = Invoke-Orbix $app @('--toggle-orb')
        $hidden = Wait-Until { -not [E2E.Native]::IsVisible($app.Hwnd) } 2500 50
        Add-Check 'orb/toggle-hides' ($hidden -ge 0) "second instance exit $($toggle.ExitCode) in $($toggle.Ms) ms"
        Start-Sleep -Milliseconds 300
        $wall = Get-RegionMean $cx0 $cy0 8
        $null = Invoke-Orbix $app @('--toggle-orb')
        $shown = Wait-Until { [E2E.Native]::IsVisible($app.Hwnd) } 2500 50
        Add-Check 'orb/toggle-shows' ($shown -ge 0) ''
        Start-Sleep -Milliseconds 700
        $idle = Get-RegionMean $cx0 $cy0 8
        Save-Shot 'orb-idle' $cx0 $cy0 60 'png'
        [E2E.Native]::MoveTo($cx0, $cy0)
        Start-Sleep -Milliseconds 500
        $hover = Get-RegionMean $cx0 $cy0 8
        Save-Shot 'orb-hover' $cx0 $cy0 60 'png'
        $dIdle = Get-ColorDistance $idle $wall
        $dHover = Get-ColorDistance $hover $wall
        Add-Info ("orb colour distance from the wallpaper: idle {0:N1}, hover {1:N1}" -f $dIdle, $dHover)
        Add-Check 'orb/idle-is-translucent' (($dIdle -gt 4) -and ($dIdle -lt $dHover)) ("idle $([int]$dIdle) < hover $([int]$dHover)")
        Add-Check 'orb/hover-is-opaque' ($dHover -gt 25) "hover distance $([int]$dHover)"
        [E2E.Native]::MoveTo(120, 120)

        # ---- open with the global hotkey --------------------------------------------------------------
        $ms = Open-ByHotkey $app
        Add-Check 'menu/hotkey-opens' ($ms -ge 0) ("window grew after {0:N1} ms" -f $ms)
        if ($ms -lt 0) {
            Add-Info ("hotkey did not open the menu; windows: " + ([E2E.Native]::ListWindows($app.Proc.Id) -join ' ; '))
            return
        }
        Add-Check 'menu/open-under-100ms' ($ms -lt 100) ("{0:N1} ms from the key press to the resized window (first open after start)" -f $ms)
        Start-Sleep -Milliseconds 400
        $r2 = Get-OverlayRect $app
        Add-Info ("menu window: {0},{1} {2}x{3}" -f $r2.L, $r2.T, $r2.W, $r2.H)
        Add-Check 'menu/stays-centered' (([math]::Abs($r2.CX - $cx0) -le 1) -and ([math]::Abs($r2.CY - $cy0) -le 1)) "centre $($r2.CX),$($r2.CY)"
        $logText = (Read-Log $app) -join "`n"
        if ($logText -match 'Menu opened: (\d+) ms') { Add-Info "app-side: menu opened in $($Matches[1]) ms to the first frame" }
        $layout = @(Get-Layout $app)
        $slotCount = if ($layout.Count -ge 1) { $layout[0].Slots.Count } else { 0 }
        Add-Check 'menu/ring-has-8-items' ($slotCount -eq 8) "rings=$($layout.Count) slots=$slotCount"
        $cornerHit = [E2E.Native]::RootWindowAt($r2.L + 2, $r2.T + 2)
        Add-Check 'menu/corner-click-through' ($cornerHit -ne $app.Hwnd) ([E2E.Native]::DescribeWindow($cornerHit))
        Save-Shot 'menu' $cx0 $cy0

        # ---- Esc closes; the hotkey toggles; the orb is the centre button -----------------------------------
        [E2E.Native]::Chord($VK.Esc)
        Add-Check 'menu/esc-closes' ((Wait-MenuClosed $app 2000) -ge 0) ''
        Start-Sleep -Milliseconds 300
        $ms = Open-ByHotkey $app
        Add-Check 'menu/reopens-fast' (($ms -ge 0) -and ($ms -lt 100)) ("{0:N1} ms" -f $ms)
        Start-Sleep -Milliseconds 400
        [E2E.Native]::Chord($Hotkey)
        Add-Check 'menu/hotkey-toggles-closed' ((Wait-MenuClosed $app 2000) -ge 0) ''
        Start-Sleep -Milliseconds 300
        [E2E.Native]::Click($cx0, $cy0)
        Add-Check 'menu/orb-click-opens' ((Wait-MenuOpen $app 2500) -ge 0) ''
        Start-Sleep -Milliseconds 700
        [E2E.Native]::Click($cx0, $cy0)
        Add-Check 'menu/orb-click-closes' ((Wait-MenuClosed $app 2500) -ge 0) ''
        Start-Sleep -Milliseconds 300

        # ---- groups: three orbits --------------------------------------------------------------------------
        $before = (Get-LayoutLines $app).Count
        $null = Open-ByHotkey $app
        Start-Sleep -Milliseconds 400
        $layout = @(Get-Layout $app)
        $folder = Get-SlotPoint $app $layout 0 1
        Add-Info ("window at the group point: " + [E2E.Native]::DescribeWindow([E2E.Native]::RootWindowAt($folder.X, $folder.Y)))
        $before = (Get-LayoutLines $app).Count
        [E2E.Native]::MoveTo($folder.X, $folder.Y)
        Add-Info ("cursor after MoveTo: " + [E2E.Native]::CursorPos())
        $ms = Wait-Layout $app $before 2 2500
        Add-Check 'menu/hover-opens-second-orbit' ($ms -ge 0) "hover on the group at $($folder.X),$($folder.Y)"
        Add-LogTrace $app 'hover'
        $layout = @(Get-Layout $app)
        if ($layout.Count -ge 2) {
            Save-Shot 'menu-group' $cx0 $cy0
            $sub = Get-SlotPoint $app $layout 1 0
            $before = (Get-LayoutLines $app).Count
            [E2E.Native]::MoveTo($sub.X, $sub.Y)
            $ms = Wait-Layout $app $before 3 2500
            Add-Check 'menu/third-orbit' ($ms -ge 0) "hover on the nested group at $($sub.X),$($sub.Y)"
            $layout = @(Get-Layout $app)
            if ($layout.Count -ge 3) {
                Save-Shot 'menu-group3' $cx0 $cy0
                $deep = Get-SlotPoint $app $layout 2 0
                [E2E.Native]::Click($deep.X, $deep.Y)
                $launched = Wait-Until { Test-Path $m2 } 6000 100
                Add-Check 'launch/item-in-third-orbit' ($launched -ge 0) "marker file after $launched ms"
                Add-Check 'launch/menu-closes-after-launch' ((Wait-MenuClosed $app 2500) -ge 0) ''
            }
        }
        if (Test-MenuOpen $app) { [void](Close-ByEsc $app) }
        Start-Sleep -Milliseconds 400

        # ---- launching a normal item, a missing file -------------------------------------------------------------
        $null = Open-ByHotkey $app
        Start-Sleep -Milliseconds 400
        $layout = @(Get-Layout $app)
        $p0 = Get-SlotPoint $app $layout 0 0
        [E2E.Native]::Click($p0.X, $p0.Y)
        Add-LogTrace $app 'launch-click'
        $launched = Wait-Until { Test-Path $m1 } 6000 100
        Add-Check 'launch/item-in-first-orbit' ($launched -ge 0) "marker file after $launched ms"
        [void](Wait-MenuClosed $app 2500)
        Start-Sleep -Milliseconds 500

        $null = Open-ByHotkey $app
        Start-Sleep -Milliseconds 400
        $layout = @(Get-Layout $app)
        $pm = Get-SlotPoint $app $layout 0 2
        Save-Shot 'menu-missing-hover' $cx0 $cy0
        [E2E.Native]::Click($pm.X, $pm.Y)
        Start-Sleep -Milliseconds 1500
        Add-Check 'launch/missing-file-does-not-crash' (-not $app.Proc.HasExited) ''
        if (Test-MenuOpen $app) { [void](Close-ByEsc $app) }
        Start-Sleep -Milliseconds 400

        # ---- search and edit mode ------------------------------------------------------------------------------------
        $before = (Get-LayoutLines $app).Count
        $null = Open-ByHotkey $app
        Start-Sleep -Milliseconds 400
        $before = (Get-LayoutLines $app).Count
        [E2E.Native]::Chord($VK.N)
        [E2E.Native]::Chord($VK.O)
        $ms = Wait-Layout $app $before 1 2500
        $layout = @(Get-Layout $app)
        $count = if ($layout.Count -ge 1) { $layout[0].Slots.Count } else { -1 }
        Add-Check 'menu/type-to-search' (($ms -ge 0) -and ($count -ge 1) -and ($count -lt 8)) "search layout has $count slots"
        Save-Shot 'menu-search' $cx0 $cy0
        [void](Close-ByEsc $app)
        Start-Sleep -Milliseconds 400

        $null = Open-ByHotkey $app
        Start-Sleep -Milliseconds 400
        $before = (Get-LayoutLines $app).Count
        [E2E.Native]::Chord([int[]]@($VK.Ctrl, $VK.E))
        $ms = Wait-Layout $app $before 1 2500
        $layout = @(Get-Layout $app)
        $count = if ($layout.Count -ge 1) { $layout[0].Slots.Count } else { -1 }
        Add-Check 'edit/ctrl-e-adds-plus-slot' (($ms -ge 0) -and ($count -eq 9)) "edit layout has $count slots"
        Save-Shot 'menu-edit' $cx0 $cy0
        [void](Close-ByEsc $app)
        Start-Sleep -Milliseconds 400

        # ---- a second start activates the first instance ------------------------------------------------------------------
        $second = Invoke-Orbix $app @()
        Add-Check 'instance/second-start-exits' ($second.Exited -and $second.ExitCode -eq 0) "exit code $($second.ExitCode) after $($second.Ms) ms"
        Add-Check 'instance/second-start-opens-menu' ((Wait-MenuOpen $app 2500) -ge 0) ''
        $count = @(Get-Process -Name 'Orbix' -ErrorAction SilentlyContinue | Where-Object { $_.Id -eq $app.Proc.Id }).Count
        Add-Check 'instance/still-one-process' ($count -eq 1) ''
        [void](Close-ByEsc $app)
        Start-Sleep -Milliseconds 500

        # ---- idle cost ---------------------------------------------------------------------------------------------------------
        Start-Sleep -Seconds 3
        [void](Measure-Idle $app 'breathing-on' 8)

        # ---- exit --------------------------------------------------------------------------------------------------------------------
        Test-LogClean $app 'basic'
        $graceful = Stop-App $app
        Add-Check 'instance/exit-command' $graceful ''
        $cfgPath = Join-Path $app.Dir 'config.json'
        $saved = $false
        try { $null = Get-Content $cfgPath -Raw | ConvertFrom-Json; $saved = $true } catch { }
        Add-Check 'config/saved-and-valid' $saved $cfgPath
    }
    finally {
        Save-LogTail $app
        if (-not $app.Proc.HasExited) { try { $app.Proc.Kill() } catch { } }
    }
}

# ------------------------------------------------------------------------------------------------- first start (defaults)

Invoke-Scenario 'fresh' {
    $app = Start-App 'fresh' ''
    try {
        Add-Check 'fresh/window-appears' ($app.Hwnd -ne [IntPtr]::Zero) ("start-to-window {0} ms" -f $app.StartMs)
        if ($app.Hwnd -eq [IntPtr]::Zero) { return }
        Start-Sleep -Milliseconds 1000
        Add-Check 'fresh/config-created' (Test-Path (Join-Path $app.Dir 'config.json')) ''
        $ms = Open-ByHotkey $app
        Add-Check 'fresh/hotkey-opens' ($ms -ge 0) ("{0:N1} ms" -f $ms)
        Start-Sleep -Milliseconds 500
        $layout = @(Get-Layout $app)
        Add-Check 'fresh/default-menu-has-8-items' (($layout.Count -ge 1) -and ($layout[0].Slots.Count -eq 8)) ''
        Save-Shot 'default-menu' $cx0 $cy0
        [void](Close-ByEsc $app)
        Test-LogClean $app 'fresh'
        Add-Check 'fresh/exit' (Stop-App $app) ''
    }
    finally {
        Save-LogTail $app
        if (-not $app.Proc.HasExited) { try { $app.Proc.Kill() } catch { } }
    }
}

# ------------------------------------------------------------------------------------------------- light theme, big coloured orb

Invoke-Scenario 'light' {
    $items = @(
        (ItemDef 'Проводник' 'app' 'explorer.exe'),
        (ItemDef 'Блокнот' 'app' 'notepad.exe'),
        (ItemDef 'Калькулятор' 'app' 'calc.exe'),
        (GroupDef 'Система' @((ItemDef 'Диспетчер задач' 'system' '' '' 'taskManager'), (ItemDef 'Показать рабочий стол' 'system' '' '' 'showDesktop'), (ItemDef 'Блокировка' 'system' '' '' 'lock'))),
        (ItemDef 'Командная строка' 'app' 'cmd.exe'),
        (ItemDef 'Paint' 'app' 'mspaint.exe')
    )
    $cfg = MakeConfig $items -Orb @{ size = 80; color = '#FF6B6B'; breathing = $false; restingOpacity = 0.6 } `
        -Menu @{ submenuOpen = 'click'; radius = 150; itemSize = 60 } `
        -Appearance @{ theme = 'light'; accent = '#0EA5E9' } -General @{ language = 'ru'; welcomeShown = $true }
    $app = Start-App 'light' $cfg
    try {
        Add-Check 'light/window-appears' ($app.Hwnd -ne [IntPtr]::Zero) ''
        if ($app.Hwnd -eq [IntPtr]::Zero) { return }
        Start-Sleep -Milliseconds 1200
        $r = Get-OverlayRect $app
        Add-Check 'light/orb-size-80' ([math]::Abs($r.W - 82) -le 3) "window $($r.W)x$($r.H)"
        Add-Check 'light/centered' (([math]::Abs($r.CX - $cx0) -le 1) -and ([math]::Abs($r.CY - $cy0) -le 1)) ''
        Save-Shot 'light-orb' $cx0 $cy0 70 'png'
        $before = (Get-LayoutLines $app).Count
        $open = Invoke-Orbix $app @('--open')
        Add-Check 'light/second-start-opens-menu' ((Wait-MenuOpen $app 3000) -ge 0) "exit code $($open.ExitCode) in $($open.Ms) ms"
        [void](Wait-Layout $app $before 1 3000)
        Save-Shot 'light-menu' $cx0 $cy0
        $layout = @(Get-Layout $app)
        if ($layout.Count -ge 1) {
            $grp = Get-SlotPoint $app $layout 0 3
            $before = (Get-LayoutLines $app).Count
            [E2E.Native]::Click($grp.X, $grp.Y)
            $ms = Wait-Layout $app $before 2 2500
            Add-Check 'light/click-opens-second-orbit' ($ms -ge 0) ''
            Add-LogTrace $app 'light-click'
            Save-Shot 'light-group' $cx0 $cy0
        }
        [void](Close-ByEsc $app)
        Start-Sleep -Seconds 3
        [void](Measure-Idle $app 'breathing-off' 8)
        Test-LogClean $app 'light'
        Add-Check 'light/exit' (Stop-App $app) ''
    }
    finally {
        Save-LogTail $app
        if (-not $app.Proc.HasExited) { try { $app.Proc.Kill() } catch { } }
    }
}

# ------------------------------------------------------------------------------------------------- settings window

Invoke-Scenario 'settings' {
    $items = @((ItemDef 'Notepad' 'app' 'notepad.exe'))
    $app = Start-App 'settings' (MakeConfig $items -General @{ language = 'en'; welcomeShown = $true })
    try {
        Add-Check 'settings/app-starts' ($app.Hwnd -ne [IntPtr]::Zero) ''
        if ($app.Hwnd -eq [IntPtr]::Zero) { return }
        Start-Sleep -Milliseconds 800
        $open = Invoke-Orbix $app @('--settings')
        $hwnd = [IntPtr]::Zero
        $null = Wait-Until {
            $found = [IntPtr]::Zero
            foreach ($w in [E2E.Native]::WindowHandles($app.Proc.Id)) {
                if ([E2E.Native]::IsVisible($w) -and [E2E.Native]::TitleOf($w).StartsWith('Orbix')) {
                    $t = [E2E.Native]::TitleOf($w)
                    if ($t -ne 'OrbixOverlay' -and $t -ne 'Orbix.MessageWindow') { $found = $w }
                }
            }
            if ($found -ne [IntPtr]::Zero) { $script:__settingsHwnd = $found; $true } else { $false }
        } 10000 100
        $hwnd = $script:__settingsHwnd
        $script:__settingsHwnd = [IntPtr]::Zero
        Add-Check 'settings/window-appears' ($hwnd -ne [IntPtr]::Zero) ("second instance exit {0} in {1} ms" -f $open.ExitCode, $open.Ms)
        if ($hwnd -ne [IntPtr]::Zero) {
            Start-Sleep -Milliseconds 700
            [E2E.Native]::Activate($hwnd)
            Start-Sleep -Milliseconds 400
            $r = [E2E.Native]::GetRect($hwnd)
            Add-Info ("settings window: {0},{1} {2}x{3}" -f $r.Left, $r.Top, $r.Width, $r.Height)
            Add-Check 'settings/has-size' (($r.Width -ge 500) -and ($r.Height -ge 400)) ''
            Save-Shot 'settings' ([int](($r.Left + $r.Right) / 2)) ([int](($r.Top + $r.Bottom) / 2)) ([int]([math]::Max($r.Width, $r.Height) / 2) + 6) 'jpg'
        }
        Test-LogClean $app 'settings'
        Add-Check 'settings/exit' (Stop-App $app) ''
    }
    finally {
        Save-LogTail $app
        if (-not $app.Proc.HasExited) { try { $app.Proc.Kill() } catch { } }
    }
}

# ------------------------------------------------------------------------------------------------- full-screen applications

Invoke-Scenario 'fullscreen' {
    $items = @((ItemDef 'Notepad' 'app' 'notepad.exe'))
    $app = Start-App 'fullscreen' (MakeConfig $items -Orb @{ visibility = 'autoHideFullscreen' })
    $child = $null
    try {
        Add-Check 'fullscreen/orb-visible-at-start' (($app.Hwnd -ne [IntPtr]::Zero) -and (Test-OrbVisible $app)) ''
        if ($app.Hwnd -eq [IntPtr]::Zero) { return }
        Start-Sleep -Milliseconds 800
        $child = Start-Child 'fullscreen'
        Add-Check 'fullscreen/test-window-created' ($child.Hwnd -ne [IntPtr]::Zero) ''
        $fg = [E2E.Native]::Foreground()
        Add-Info ("foreground after the full-screen window appeared: " + [E2E.Native]::DescribeWindow($fg))
        $hidden = Wait-Until { -not [E2E.Native]::IsVisible($app.Hwnd) } 5000 100
        Add-Check 'fullscreen/orb-hides' ($hidden -ge 0) "hidden after $hidden ms"
        Stop-Child $child
        $shown = Wait-Until { Test-OrbVisible $app } 5000 100
        Add-Check 'fullscreen/orb-returns' ($shown -ge 0) "visible again after $shown ms"
        Test-LogClean $app 'fullscreen'
        Add-Check 'fullscreen/exit' (Stop-App $app) ''
    }
    finally {
        Stop-Child $child
        Save-LogTail $app
        if (-not $app.Proc.HasExited) { try { $app.Proc.Kill() } catch { } }
    }
}

Invoke-Scenario 'desktop-only' {
    $items = @((ItemDef 'Notepad' 'app' 'notepad.exe'))
    $app = Start-App 'desktop' (MakeConfig $items -Orb @{ visibility = 'desktopOnly' })
    $child = $null
    try {
        Start-Sleep -Milliseconds 1200
        Add-Check 'desktop-only/visible-over-desktop' (($app.Hwnd -ne [IntPtr]::Zero) -and (Test-OrbVisible $app)) ''
        if ($app.Hwnd -eq [IntPtr]::Zero) { return }
        $child = Start-Child 'window'
        Add-Check 'desktop-only/test-window-created' ($child.Hwnd -ne [IntPtr]::Zero) ''
        if ($child.Hwnd -ne [IntPtr]::Zero) {
            $null = Wait-Until { [E2E.Native]::GetRect($child.Hwnd).Width -gt 100 } 3000 50
            $cr = [E2E.Native]::GetRect($child.Hwnd)
            Add-Info ("child alive=" + (-not $child.Process.HasExited) + "; windows: " + ([E2E.Native]::ListWindows($child.Process.Id) -join ' ; '))
            Add-Info ("child rect: {0},{1} {2}x{3}; window at centre: {4}" -f $cr.Left, $cr.Top, $cr.Width, $cr.Height, ([E2E.Native]::DescribeWindow([E2E.Native]::RootWindowAt($cx0, $cy0))))
        }
        $hidden = Wait-Until { -not [E2E.Native]::IsVisible($app.Hwnd) } 5000 100
        Add-Check 'desktop-only/orb-hides-behind-a-window' ($hidden -ge 0) "hidden after $hidden ms"
        Add-LogTrace $app 'desktop-only'
        Stop-Child $child
        $shown = Wait-Until { Test-OrbVisible $app } 5000 100
        Add-Check 'desktop-only/orb-returns' ($shown -ge 0) "visible again after $shown ms"
        Test-LogClean $app 'desktop-only'
        Add-Check 'desktop-only/exit' (Stop-App $app) ''
    }
    finally {
        Stop-Child $child
        Save-LogTail $app
        if (-not $app.Proc.HasExited) { try { $app.Proc.Kill() } catch { } }
    }
}

# ------------------------------------------------------------------------------------------------- damaged configuration

Invoke-Scenario 'corrupt-config' {
    $app = Start-App 'corrupt' '{ this is : not json ,,, '
    try {
        Add-Check 'corrupt/app-starts-anyway' (($app.Hwnd -ne [IntPtr]::Zero) -and (-not $app.Proc.HasExited)) ''
        if ($app.Hwnd -eq [IntPtr]::Zero) { return }
        Start-Sleep -Milliseconds 800
        $saved = @(Get-ChildItem $app.Dir -Filter 'config.corrupted-*.json' -ErrorAction SilentlyContinue)
        Add-Check 'corrupt/broken-file-is-kept' ($saved.Count -ge 1) ''
        $ms = Open-ByHotkey $app
        $layout = @(Get-Layout $app)
        Add-Check 'corrupt/defaults-are-used' (($ms -ge 0) -and ($layout.Count -ge 1) -and ($layout[0].Slots.Count -eq 8)) ''
        [void](Close-ByEsc $app)
        Add-Check 'corrupt/exit' (Stop-App $app) ''
    }
    finally {
        Save-LogTail $app
        if (-not $app.Proc.HasExited) { try { $app.Proc.Kill() } catch { } }
    }
}

# ------------------------------------------------------------------------------------------------- report

$failed = @($script:Results | Where-Object { -not $_.Ok })
$lines = @($script:Results | ForEach-Object { '[{0}] {1} {2}' -f $(if ($_.Ok) { 'PASS' } else { 'FAIL' }), $_.Name, $_.Detail })
$summary = "checks={0} passed={1} failed={2} seconds={3}" -f $script:Results.Count, ($script:Results.Count - $failed.Count), $failed.Count, [int]$script:Clock.Elapsed.TotalSeconds
Set-Content (Join-Path $Out 'report.txt') (($summary, '') + $lines + @('') + $script:Info) -Encoding UTF8

Write-Annotation 'e2e-summary' ($summary + "`n" + (($failed | ForEach-Object { 'FAIL ' + $_.Name + ' ' + $_.Detail }) -join "`n"))
Write-Annotation 'e2e-report' ($lines -join "`n")
Write-Annotation 'e2e-info' ($script:Info -join "`n")
Write-Annotation 'e2e-logs' (($script:LogTails -join "`n`n"))

foreach ($child in $script:Children) { Stop-Child ([pscustomobject]@{ Process = $child }) }
if ($failed.Count -gt 0) { exit 1 }
