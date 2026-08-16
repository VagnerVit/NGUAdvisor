# Reload the advisor into the running game without touching its UI.
#
#   pwsh build/reload-advisor.ps1              unload the live build, rebuild, inject
#   pwsh build/reload-advisor.ps1 -UnloadOnly  just unload
#
# NEVER use `smi.exe eject` for this. Both ways of asking it killed a live game on 2026-08-13:
#   eject -m Unload         runs Unity/WinForms teardown on the injector's thread (main-thread rule).
#   eject -m RequestUnload  returns at once, and eject then unloads the assembly out from under a
#                           MonoBehaviour still executing it.
# `eject` needs a synchronous teardown; a safe teardown cannot be synchronous from outside.
#
# So the unload is asked for with a FILE. Main.Update() polls it on the Unity thread, deletes it as
# the acknowledgement, and tears itself down. The file vanishing is what this script waits for.
#
# Injecting over a LIVE instance is the thing to avoid: Mono cannot unload an assembly and
# Loader.Init refuses a second Main, so the old build keeps running and only says so in a Unity
# warning. Always verify afterwards — the first debug.log line carries the build tag.

param([switch]$UnloadOnly)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$lowLow = Join-Path $env:USERPROFILE "AppData\LocalLow\NGUAdvisor"
$request = Join-Path $lowLow "unload.request"
$marker = Join-Path $lowLow "injected.txt"

if (-not (Get-Process NGUIdle -ErrorAction SilentlyContinue)) { throw "NGU Idle is not running" }

# The marker is written by Loader.Init and deleted by Loader.Unload, so it answers "is an advisor
# live in THIS process?" — and the pid inside distinguishes a live marker from one a crash left behind.
$live = $false
if (Test-Path $marker) {
    $pidLine = (Get-Content $marker | Where-Object { $_ -like "pid=*" }) -replace "pid=", ""
    $live = [bool](Get-Process -Id ([int]$pidLine) -ErrorAction SilentlyContinue)
}

if ($live) {
    Write-Output "==> requesting unload"
    New-Item -ItemType File -Path $request -Force | Out-Null
    $waited = 0
    while ((Test-Path $request) -and $waited -lt 15) { Start-Sleep -Seconds 1; $waited++ }
    if (Test-Path $request) {
        Remove-Item $request -Force
        throw "the advisor never picked up the unload request (older build without file-based unload?) — click Unload Advisor in Settings, or restart the game"
    }
    Start-Sleep -Seconds 2
    Write-Output "    unloaded"
} else {
    Write-Output "==> no advisor live in this process"
}

if ($UnloadOnly) { exit 0 }

Write-Output "==> build + inject"
$bash = "C:\Program Files\Git\bin\bash.exe"
$log = Join-Path $lowLow "logs\debug.log"

# Destroying the host GameObject is DEFERRED, and observed to take minutes with the window closed:
# three injects in a row were refused with "already injected" while the object lingered, each of them
# reporting success. So: inject, verify the build tag actually changed, retry if it did not.
$ok = $false
for ($attempt = 1; $attempt -le 4 -and -not $ok; $attempt++) {
    $out = & $bash -lc "cd '$($root -replace '\\','/' -replace '^C:','/c')' && ./package-release.sh --inject" 2>&1
    $built = ($out | Select-String "built: NGUAdvisor\.r(\d{6})(\d{4})").Matches
    $want = if ($built) { "$($built[0].Groups[1].Value)-$($built[0].Groups[2].Value)" } else { $null }
    $out | Where-Object { $_ -match "built:|Injected|ERROR" } | ForEach-Object { Write-Output "    $_" }

    Start-Sleep -Seconds 6
    # The LAST writer-alive line, not the first: debug.log is appended to, never truncated, so the
    # first line is whatever build started the file — reading it always "confirmed" a stale build.
    $alive = (Select-String -Path $log -Pattern "writer alive" | Select-Object -Last 1).Line
    if ($want -and $alive -match [regex]::Escape($want)) {
        $ok = $true
        Write-Output "==> live: $alive"
    } else {
        Write-Output "==> attempt ${attempt}: expected build $want, log says: $alive"
        if ($attempt -lt 4) { Write-Output "    host object not released yet — retrying in 20s"; Start-Sleep -Seconds 20 }
    }
}
if (-not $ok) { throw "the new build never took over — restart NGU Idle and inject again" }
