# decomp-diff.ps1 — game-update breakage detector for NGUAdvisor.
#
# The injector calls game internals directly (Character, controllers, drop tables) and
# replicates game math (counterfeit %, pit tiers, fruit growth). A game patch can silently
# break any of it. This script:
#   1. hashes the game's Assembly-CSharp.dll against the recorded baseline hash — unchanged
#      game = nothing to do;
#   2. on change, re-decompiles with ilspycmd and verifies every line of api-manifest.txt
#      (the injector's full game-API surface) against the fresh sources;
#   3. lists which watched files changed at all (signature intact but body edits = review);
#   4. -RefreshBaseline replaces reference\decomp-full with the fresh decompile and records
#      the new hash once you've reviewed the report.
#
# Usage:  .\decomp-diff.ps1              (check)
#         .\decomp-diff.ps1 -Force       (re-verify even if hash unchanged)
#         .\decomp-diff.ps1 -RefreshBaseline   (accept new game version as baseline)

param(
    [string]$GameDll = "D:\SteamLibrary\steamapps\common\NGU IDLE\NGUIdle_Data\Managed\Assembly-CSharp.dll",
    [switch]$Force,
    [switch]$RefreshBaseline
)

$ErrorActionPreference = "Stop"
$baseline = Join-Path $PSScriptRoot "..\..\reference\decomp-full" | Resolve-Path
$manifest = Join-Path $PSScriptRoot "api-manifest.txt"
$hashFile = Join-Path $baseline "_source.sha256"

if (-not (Test-Path $GameDll)) { Write-Host "GAME DLL NOT FOUND: $GameDll" -ForegroundColor Red; exit 3 }

$curHash = (Get-FileHash $GameDll -Algorithm SHA256).Hash
$oldHash = if (Test-Path $hashFile) { (Get-Content $hashFile -TotalCount 1).Trim() } else { $null }

if (-not $oldHash) {
    # Bootstrap: baseline decomp came from the currently-installed game version.
    Set-Content $hashFile $curHash
    Write-Host "Baseline hash recorded ($($curHash.Substring(0,12)))." -ForegroundColor Green
    $oldHash = $curHash
}

if ($curHash -eq $oldHash -and -not $Force) {
    Write-Host "Game unchanged ($($curHash.Substring(0,12))) - injector API surface safe." -ForegroundColor Green
    exit 0
}

if ($curHash -ne $oldHash) {
    Write-Host "GAME UPDATED: $($oldHash.Substring(0,12)) -> $($curHash.Substring(0,12))" -ForegroundColor Yellow
}

# Fresh decompile.
$tmp = Join-Path $env:TEMP "ngu-decomp-$($curHash.Substring(0,8))"
if (-not (Test-Path (Join-Path $tmp "Character.cs"))) {
    Write-Host "Decompiling (ilspycmd)..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force $tmp | Out-Null
    & ilspycmd -p -o $tmp $GameDll | Out-Null
    # Project mode nests sources; flatten lookup by filename.
}
$freshFiles = @{}
Get-ChildItem $tmp -Recurse -Filter *.cs | ForEach-Object { $freshFiles[$_.Name] = $_.FullName }

# Verify the manifest.
$missing = @()
$okCount = 0
$watched = @{}
foreach ($line in Get-Content $manifest) {
    $line = $line.Trim()
    if (-not $line -or $line.StartsWith("#")) { continue }
    $parts = $line -split "`t", 2
    if ($parts.Count -ne 2) { Write-Host "BAD MANIFEST LINE: $line" -ForegroundColor Red; continue }
    $file, $needle = $parts[0].Trim(), $parts[1].Trim()
    $watched[$file] = $true
    if (-not $freshFiles.ContainsKey($file)) {
        $missing += "MISSING FILE: $file (needed for: $needle)"
        continue
    }
    $hit = Select-String -Path $freshFiles[$file] -SimpleMatch $needle -Quiet
    if ($hit) { $okCount++ } else { $missing += "MISSING: $file :: $needle" }
}

# Which watched files changed at all (body edits with intact signatures still deserve review).
$changed = @()
foreach ($file in $watched.Keys | Sort-Object) {
    $old = Join-Path $baseline $file
    if ((Test-Path $old) -and $freshFiles.ContainsKey($file)) {
        $h1 = (Get-FileHash $old -Algorithm SHA256).Hash
        $h2 = (Get-FileHash $freshFiles[$file] -Algorithm SHA256).Hash
        if ($h1 -ne $h2) { $changed += $file }
    }
}

Write-Host ""
Write-Host "=== decomp-diff report ===" -ForegroundColor Cyan
Write-Host "Manifest entries OK: $okCount"
if ($missing.Count -gt 0) {
    Write-Host "BROKEN ($($missing.Count)):" -ForegroundColor Red
    $missing | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
}
if ($changed.Count -gt 0) {
    Write-Host "Watched files with body changes (review recommended):" -ForegroundColor Yellow
    $changed | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
}
if ($missing.Count -eq 0 -and $changed.Count -eq 0) {
    Write-Host "All watched APIs intact." -ForegroundColor Green
}

if ($RefreshBaseline) {
    Write-Host ""
    Write-Host "Refreshing baseline decomp..." -ForegroundColor Cyan
    robocopy $tmp $baseline /MIR /NFL /NDL /NJH /NJS | Out-Null
    Set-Content $hashFile $curHash
    Write-Host "Baseline now $($curHash.Substring(0,12))." -ForegroundColor Green
}

exit $(if ($missing.Count -gt 0) { 1 } elseif ($changed.Count -gt 0) { 2 } else { 0 })
