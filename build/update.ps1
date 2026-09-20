<#
.SYNOPSIS
    Checks github.com/VagnerVit/NGUAdvisor for a newer release and installs it.

.DESCRIPTION
    Ships INSIDE the release zip as injector\update.ps1 and is driven from two places:

      -Check   the running advisor spawns it every few hours (Managers/UpdateChecker.cs) and
               reads the result out of update.state. Network only, nothing is replaced.
      -Apply   "Run NGU Advisor.bat" runs it right before injecting, so a pending update lands
               on the DLL that is about to be loaded.

    WHY POWERSHELL AND NOT C#. The advisor lives in NGU Idle's Unity 2019.4 Mono domain, whose
    TLS stack and root certificate store cannot be relied on to reach api.github.com. Windows
    PowerShell is outside that domain and has the host's real trust store, so no HTTPS client
    ever has to exist inside the game process.

    VERSION TRUTH COMES FROM THE ADVISOR, NEVER FROM THE DLL ON DISK. Main.Version is the one
    hand-bumped SemVer; the assembly attributes in Properties/AssemblyInfo.cs drifted long ago
    (they still read 1.2.2) and the assembly NAME carries a build timestamp, not a version. So
    -Check is passed -CurrentVersion by the advisor and records it as installed= in update.state;
    -Apply reads that line back. The game's own version is irrelevant here and is never read.

    NOTHING HERE MAY STOP THE GAME FROM STARTING. Every failure path logs and exits 0, leaving
    the existing DLL in place — the launcher injects either way.
#>
[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$Apply,
    # The running advisor's Main.Version. Absent on an -Apply run, where the baseline comes from
    # update.state instead.
    [string]$CurrentVersion,
    # Ignore the six-hour throttle (manual runs while diagnosing).
    [switch]$Force
)

# This fork, NOT the upstream it was taken from: upstream moved on to a 2.x "Companion" that is a
# different program. Hard-coded for the same reason package-release.sh hard-codes it.
$Repo = 'VagnerVit/NGUAdvisor'

# Re-ask github at most this often. The unauthenticated API allows 60 requests/hour per IP and an
# idle game runs for days, so a check every few hours is free and a check every launch is not.
$CheckIntervalHours = 6

$InjectorDir = $PSScriptRoot
$DllPath     = Join-Path $InjectorDir 'NGUAdvisor.dll'
$DataDir     = Join-Path $env:USERPROFILE 'AppData\LocalLow\NGUAdvisor'
$StatePath   = Join-Path $DataDir 'update.state'
$MarkerPath  = Join-Path $DataDir 'injected.txt'
$LogPath     = Join-Path $DataDir 'logs\update.log'

function Write-Log([string]$msg) {
    try {
        $dir = Split-Path -Parent $LogPath
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        Add-Content -Path $LogPath -Value ("{0:yyyy-MM-dd HH:mm:ss} {1}" -f (Get-Date), $msg)
    } catch { }
}

# update.state is key=value, deliberately the same shape as Loader's injected.txt: one writer
# (this script), no parser needed on either side, and readable when something goes wrong.
function Read-State {
    $state = @{}
    if (Test-Path $StatePath) {
        foreach ($line in (Get-Content -Path $StatePath -ErrorAction SilentlyContinue)) {
            $i = $line.IndexOf('=')
            if ($i -gt 0) { $state[$line.Substring(0, $i)] = $line.Substring($i + 1) }
        }
    }
    return $state
}

function Write-State([hashtable]$state) {
    if (-not (Test-Path $DataDir)) { New-Item -ItemType Directory -Path $DataDir -Force | Out-Null }
    $lines = foreach ($k in ($state.Keys | Sort-Object)) { "$k=$($state[$k])" }
    Set-Content -Path $StatePath -Value $lines -Encoding ASCII
}

# A cast in try/catch, not ::TryParse: the [ref] overloads cannot bind an untyped $null here, which
# is how the first version of this script silently failed every check.
function ConvertTo-Version([string]$text) {
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    try { return [version]$text.Trim().TrimStart('v', 'V') } catch { return $null }
}

# The baseline for "is there something newer": the advisor's own Main.Version, by way of whoever
# last told us. An -Apply run has no advisor to ask, so it falls back through what the advisor
# wrote earlier — update.state first, then the inject marker.
function Get-InstalledVersion([hashtable]$state) {
    $v = ConvertTo-Version $CurrentVersion
    if ($v) { return $v }
    $v = ConvertTo-Version $state['installed']
    if ($v) { return $v }
    if (Test-Path $MarkerPath) {
        foreach ($line in (Get-Content -Path $MarkerPath -ErrorAction SilentlyContinue)) {
            if ($line -like 'version=*') { return ConvertTo-Version $line.Substring(8) }
        }
    }
    return $null
}

function Get-LatestRelease {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    # -UseBasicParsing: the IE engine is not available on a machine that never launched IE.
    return Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" `
        -Headers @{ 'User-Agent' = 'NGUAdvisor-Updater' } -TimeoutSec 20 -UseBasicParsing
}

try {
    $state = Read-State
    $installed = Get-InstalledVersion $state
    if ($CurrentVersion) { $state['installed'] = $CurrentVersion.Trim() }

    $lastCheck = [datetime]::MinValue
    if ($state['checkedUtc']) {
        try { $lastCheck = [datetime]::Parse($state['checkedUtc'], $null, [Globalization.DateTimeStyles]::RoundtripKind) } catch { }
    }
    $fresh = ((Get-Date).ToUniversalTime() - $lastCheck.ToUniversalTime()).TotalHours -lt $CheckIntervalHours

    if ($fresh -and -not $Force) {
        # Still record a newly reported installed version, then stop before touching the network.
        if ($CurrentVersion) { Write-State $state }
        if (-not $Apply) { exit 0 }
    } else {
        $rel = Get-LatestRelease
        $latest = ConvertTo-Version $rel.tag_name
        $asset = $rel.assets | Where-Object { $_.name -like 'dist_*.zip' } | Select-Object -First 1

        $newer = $latest -and $installed -and $asset -and $latest -gt $installed

        $state['checkedUtc'] = (Get-Date).ToUniversalTime().ToString('o')
        $state['latest']     = if ($latest) { $latest.ToString() } else { '' }
        $state['url']        = if ($asset) { $asset.browser_download_url } else { '' }
        $state['available']  = if ($newer) { '1' } else { '0' }
        Write-State $state
        Write-Log "checked: installed=$installed latest=$latest available=$($state['available'])"
    }

    if (-not $Apply) { exit 0 }

    # ---- -Apply -------------------------------------------------------------------------------
    $state = Read-State
    if ($state['available'] -ne '1' -or -not $state['url']) { exit 0 }
    if (-not (Test-Path $DllPath)) { Write-Log "apply skipped: $DllPath is missing"; exit 0 }

    $version = $state['latest']
    # An advisor that is still RUNNING the old build keeps reporting the old version, so available=
    # legitimately flips back to 1 between the update and the relaunch that picks it up. Saying so in
    # the footer is honest; downloading the same zip a second time is not.
    if ($state['applied'] -eq $version) { exit 0 }
    $temp = Join-Path ([IO.Path]::GetTempPath()) ("NGUAdvisor-update-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temp -Force | Out-Null
    try {
        $zip = Join-Path $temp 'release.zip'
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $state['url'] -OutFile $zip -TimeoutSec 180 `
            -Headers @{ 'User-Agent' = 'NGUAdvisor-Updater' } -UseBasicParsing
        Expand-Archive -Path $zip -DestinationPath $temp -Force

        # package-release.sh zips the STAGE FOLDER, so the archive holds NGUAdvisor-<version>\injector\…
        # rather than injector\… at the root. Search instead of assuming either layout.
        $new = Get-ChildItem -Path $temp -Recurse -Filter 'NGUAdvisor.dll' |
               Where-Object { $_.Length -gt 0 } | Select-Object -First 1
        if (-not $new) { Write-Log "apply aborted: no NGUAdvisor.dll inside $($state['url'])"; exit 0 }

        # Backup first and restore it on any failure: a half-written DLL is an instant crash on
        # inject, and the old one was working a second ago.
        $backup = "$DllPath.bak"
        Copy-Item -Path $DllPath -Destination $backup -Force
        try {
            Copy-Item -Path $new.FullName -Destination $DllPath -Force
        } catch {
            Copy-Item -Path $backup -Destination $DllPath -Force
            throw
        }

        # Update the updater. PowerShell parses a script fully before running it, so overwriting this
        # file mid-run is safe — and without it a bug shipped here could never be fixed by an update.
        # "Run NGU Advisor.bat" is deliberately NOT refreshed: cmd.exe reads a batch file line by line
        # as it executes, and this script is running FROM that batch file. A launcher change therefore
        # needs a manual re-download; see BUILD.md.
        $newScript = Get-ChildItem -Path $temp -Recurse -Filter 'update.ps1' | Select-Object -First 1
        if ($newScript) { Copy-Item -Path $newScript.FullName -Destination $PSCommandPath -Force }

        # smi.exe and SharpMonoInjector.dll are third-party tools that ship with the zip only to make
        # it runnable; they are not ours to replace. Sample profiles are left alone too — they may
        # have been edited in place.
        $state['installed'] = $version
        $state['applied'] = $version
        $state['available'] = '0'
        $state['appliedUtc'] = (Get-Date).ToUniversalTime().ToString('o')
        Write-State $state
        Write-Log "applied $version"
        Write-Host "NGU Advisor updated to $version."
    } finally {
        Remove-Item -Path $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
} catch {
    Write-Log "failed: $($_.Exception.Message)"
}

# ALWAYS 0. An update problem must never be the reason the game does not get its advisor.
exit 0
