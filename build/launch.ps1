<#
.SYNOPSIS
    Starts NGU Advisor: installs a pending update, starts NGU Idle if needed, injects, confirms.

.DESCRIPTION
    Ships as injector\launch.ps1 and is run by "Run NGU Advisor.bat". The batch file stays a
    one-liner on purpose: cmd.exe reads a .bat while executing it, so the updater can never replace
    it, but it CAN replace this script. Everything the launcher does therefore lives here.

    Runs under Windows PowerShell 5.1 (every player has it; pwsh is optional). Without a console to
    read keys from (redirected input) it never prompts and takes the default answer.

    -Reload   replace an advisor that is already running instead of asking
    -NoUpdate skip the update step (used for the re-run after an update replaced this script)
#>
[CmdletBinding()]
param(
    [switch]$Reload,
    [switch]$NoUpdate
)

$ErrorActionPreference = 'Stop'

$SteamAppId   = 1147690
$GameProcess  = 'NGUIdle'
$InjectorDir  = $PSScriptRoot
$InstallDir   = Split-Path -Parent $InjectorDir
$DllPath      = Join-Path $InjectorDir 'NGUAdvisor.dll'
$SmiPath      = Join-Path $InjectorDir 'smi.exe'
$UpdaterPath  = Join-Path $InjectorDir 'update.ps1'
$DataDir      = Join-Path $env:USERPROFILE 'AppData\LocalLow\NGUAdvisor'
$MarkerPath   = Join-Path $DataDir 'injected.txt'
$UnloadPath   = Join-Path $DataDir 'unload.request'
$StatePath    = Join-Path $DataDir 'update.state'
$LogDir       = Join-Path $DataDir 'logs'
$Interactive  = -not [Console]::IsInputRedirected

# Mono needs a moment after the game window appears before an injected assembly can find its classes.
$GameSettleSeconds = 8
$TotalSteps = 4

try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }
try { $Host.UI.RawUI.WindowTitle = 'NGU Advisor' } catch { }

# ---------------------------------------------------------------- output

$Accent = 'Cyan'

function Write-Banner([string]$Version) {
    $title = 'N G U   A D V I S O R'
    $sub = if ($Version) { "v$Version" } else { 'launcher' }
    $width = 44
    $line = '═' * $width
    Write-Host ''
    Write-Host "  ╔$line╗" -ForegroundColor $Accent
    Write-Host "  ║$($title.PadLeft(($width + $title.Length) / 2).PadRight($width))║" -ForegroundColor $Accent
    Write-Host "  ║$($sub.PadLeft(($width + $sub.Length) / 2).PadRight($width))║" -ForegroundColor DarkCyan
    Write-Host "  ╚$line╝" -ForegroundColor $Accent
    Write-Host ''
}

$script:Step = 0
function Write-Step([string]$Text) {
    $script:Step++
    Write-Host "  [$script:Step/$TotalSteps] " -ForegroundColor $Accent -NoNewline
    Write-Host $Text -ForegroundColor White
}

function Write-Result([string]$Tag, [string]$Color, [string]$Text) {
    Write-Host '        ' -NoNewline
    Write-Host $Tag.PadRight(5) -ForegroundColor $Color -NoNewline
    Write-Host " $Text" -ForegroundColor Gray
}
function Write-Ok([string]$Text)   { Write-Result 'OK' 'Green' $Text }
function Write-Info([string]$Text) { Write-Result '..' 'DarkGray' $Text }
function Write-Warn([string]$Text) { Write-Result '!!' 'Yellow' $Text }
function Write-Fail([string]$Text) { Write-Result 'FAIL' 'Red' $Text }

# Polls $Condition until it returns something truthy or the timeout passes; shows a spinner meanwhile.
function Wait-Until([scriptblock]$Condition, [int]$TimeoutSeconds, [string]$Label) {
    $animate = -not [Console]::IsOutputRedirected
    $frames = '|', '/', '-', '\'
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $i = 0
    while ((Get-Date) -lt $deadline) {
        $result = & $Condition
        if ($result) { if ($animate) { Write-Host "`r$(' ' * 70)`r" -NoNewline }; return $result }
        $left = [int]($deadline - (Get-Date)).TotalSeconds
        if ($animate) { Write-Host "`r        $($frames[$i % 4]) $Label ($left s)   " -ForegroundColor DarkGray -NoNewline }
        Start-Sleep -Milliseconds 250
        $i++
    }
    if ($animate) { Write-Host "`r$(' ' * 70)`r" -NoNewline }
    return $null
}

# One key from the user, or $Default without a console. Keys are matched case-insensitively.
function Read-Choice([string]$Prompt, [string[]]$Keys, [string]$Default) {
    if (-not $Interactive) { return $Default }
    Write-Host ''
    Write-Host "        $Prompt " -ForegroundColor Yellow -NoNewline
    while ($true) {
        $key = [Console]::ReadKey($true)
        $char = ([string]$key.KeyChar).ToUpperInvariant()
        if ($key.Key -eq 'Enter') { $char = $Default }
        if ($Keys -contains $char) { Write-Host $char -ForegroundColor White; return $char }
    }
}

function Exit-Launcher([int]$Code) {
    if ($Interactive) {
        Write-Host ''
        if ($Code -eq 0) {
            Write-Host '        This window closes in 5 s - press any key to keep it open.' -ForegroundColor DarkGray
            $deadline = (Get-Date).AddSeconds(5)
            while ((Get-Date) -lt $deadline) {
                if ([Console]::KeyAvailable) {
                    [void][Console]::ReadKey($true)
                    Write-Host '        Press any key to close.' -ForegroundColor DarkGray
                    [void][Console]::ReadKey($true)
                    break
                }
                Start-Sleep -Milliseconds 100
            }
        } else {
            Write-Host "        Logs: $LogDir" -ForegroundColor DarkGray
            Write-Host '        Press any key to close.' -ForegroundColor DarkGray
            [void][Console]::ReadKey($true)
        }
    }
    exit $Code
}

# ---------------------------------------------------------------- state

function Read-KeyValueFile([string]$Path) {
    $values = @{}
    if (Test-Path $Path) {
        foreach ($line in (Get-Content -Path $Path -ErrorAction SilentlyContinue)) {
            $i = $line.IndexOf('=')
            if ($i -gt 0) { $values[$line.Substring(0, $i)] = $line.Substring($i + 1).Trim() }
        }
    }
    return $values
}

# DLLs before 1.9.0 carry a stale 1.2.2 product version, so those fall back to what the updater recorded.
function Get-InstalledVersion {
    try {
        $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($DllPath).ProductVersion -as [version]
        if ($fileVersion -and $fileVersion -ge [version]'1.9.0') { return $fileVersion.ToString() }
    } catch { }
    return (Read-KeyValueFile $StatePath)['installed']
}

function Get-Game { Get-Process -Name $GameProcess -ErrorAction SilentlyContinue | Select-Object -First 1 }

# The marker Loader.Init writes, but only when it belongs to the running game (a crash leaves a stale one).
function Get-LiveAdvisor([int]$GamePid) {
    $marker = Read-KeyValueFile $MarkerPath
    if ($marker['pid'] -and [int]$marker['pid'] -eq $GamePid) { return $marker }
    return $null
}

# ---------------------------------------------------------------- steps

function Invoke-UpdateStep {
    Write-Step 'Checking for updates'
    if ($NoUpdate -or -not (Test-Path $UpdaterPath)) { Write-Info 'skipped'; return $false }

    $before = Read-KeyValueFile $StatePath
    $updater = Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden -PassThru `
        -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$UpdaterPath`"", '-Apply')
    [void](Wait-Until { $updater.HasExited } 240 'asking GitHub')
    $after = Read-KeyValueFile $StatePath

    if ($after['applied'] -and $after['applied'] -ne $before['applied']) {
        Write-Ok "updated to v$($after['applied'])"
        return $true
    }
    if ($after['failedUtc'] -and $after['failedUtc'] -ne $before['failedUtc']) {
        Write-Warn 'GitHub not reachable - starting the installed version'
    } else {
        Write-Ok 'up to date'
    }
    return $false
}

function Invoke-GameStep {
    Write-Step 'Finding NGU Idle'
    $game = Get-Game
    if ($game) { Write-Ok "running (pid $($game.Id))"; return $game }

    Write-Info 'not running - starting it through Steam'
    Start-Process "steam://rungameid/$SteamAppId"
    $game = Wait-Until { $g = Get-Game; if ($g -and $g.MainWindowHandle -ne 0) { $g } } 180 'waiting for the game window'
    if (-not $game) { Write-Fail 'NGU Idle did not start - start it yourself and run the launcher again'; Exit-Launcher 1 }
    [void](Wait-Until { $false } $GameSettleSeconds 'letting the game load')
    Write-Ok "started (pid $($game.Id))"
    return $game
}

# $true when the slot is free to inject into.
function Invoke-ExistingAdvisorStep($Game) {
    Write-Step 'Checking for a running advisor'
    $live = Get-LiveAdvisor $Game.Id
    if (-not $live) { Write-Ok 'none - ready to inject'; return $true }

    Write-Warn "v$($live['version']) (build $($live['build'])) is already running"
    $default = if ($Reload) { 'R' } else { 'K' }
    $choice = if ($Reload) { 'R' } else { Read-Choice '[R] replace it with this version   [K] keep it  >' @('R', 'K') $default }
    if ($choice -ne 'R') { Write-Info 'kept the running advisor'; return $false }

    # Never smi.exe eject: it tears the advisor down off the Unity thread and has crashed the game.
    # The advisor polls this file on its own thread and deletes it once it has shut down.
    New-Item -ItemType File -Path $UnloadPath -Force | Out-Null
    $unloaded = Wait-Until { -not (Test-Path $UnloadPath) } 20 'asking the advisor to shut down'
    if (-not $unloaded) {
        Remove-Item $UnloadPath -Force -ErrorAction SilentlyContinue
        Write-Fail 'the running advisor did not shut down - use Unload Advisor in its Settings, or restart the game'
        Exit-Launcher 1
    }
    Write-Ok 'previous advisor shut down'
    return $true
}

function Invoke-InjectStep($Game) {
    Write-Step 'Injecting the advisor'
    # Loader.Init refuses to start while the previous advisor's host object still exists, and Unity
    # destroys it with a delay - so a refused inject is retried rather than reported as a failure.
    for ($attempt = 1; $attempt -le 4; $attempt++) {
        $since = (Get-Date).ToUniversalTime()
        $output = & $SmiPath inject -p $GameProcess -a $DllPath -n NGUAdvisor -c Loader -m Init 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Fail "smi.exe failed: $(($output | Out-String).Trim())"
            Exit-Launcher 1
        }
        $marker = Wait-Until {
            $m = Get-LiveAdvisor $Game.Id
            if ($m -and $m['injectedUtc'] -and [datetime]::Parse($m['injectedUtc']).ToUniversalTime() -ge $since.AddSeconds(-1)) { $m }
        } 20 'waiting for the advisor to start'
        if ($marker) { Write-Ok "build $($marker['build']) started"; return $marker }
        if ($attempt -lt 4) {
            [void](Wait-Until { $false } 15 "previous advisor still closing - retry $($attempt + 1) of 4")
        }
    }
    Write-Fail 'the advisor did not start - restart NGU Idle and run the launcher again'
    Exit-Launcher 1
}

# ---------------------------------------------------------------- main

try {
    Write-Banner (Get-InstalledVersion)

    # The advisor is loaded from bytes and cannot see this folder; this file is how its update check finds update.ps1.
    New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
    Set-Content -Path (Join-Path $DataDir 'install.txt') -Value ($InstallDir.TrimEnd('\') + '\') -Encoding ASCII

    if (Invoke-UpdateStep) {
        # The update may have replaced this very script; continue in the new copy.
        $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-NoUpdate')
        if ($Reload) { $arguments += '-Reload' }
        & powershell.exe @arguments
        exit $LASTEXITCODE
    }

    $game = Invoke-GameStep
    if (-not (Invoke-ExistingAdvisorStep $game)) { Exit-Launcher 0 }
    $marker = Invoke-InjectStep $game

    Write-Host ''
    Write-Host '        ' -NoNewline
    Write-Host " NGU Advisor v$($marker['version']) is running " -ForegroundColor Black -BackgroundColor Green -NoNewline
    Write-Host "  build $($marker['build'])" -ForegroundColor DarkGray
    Write-Host '        Press ' -ForegroundColor Gray -NoNewline
    Write-Host 'F1' -ForegroundColor $Accent -NoNewline
    Write-Host ' in the game to open it.' -ForegroundColor Gray
    Exit-Launcher 0
} catch {
    Write-Host ''
    Write-Fail $_.Exception.Message
    Exit-Launcher 1
}
