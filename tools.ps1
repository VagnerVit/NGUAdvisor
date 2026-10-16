<#
.SYNOPSIS
    The one entry point for building, testing, deploying and releasing NGU Advisor.

.DESCRIPTION
    ./tools.ps1 build              Release build -> NGUAdvisor/bin/Release/net48/NGUAdvisor.r<stamp>.dll
    ./tools.ps1 test               unit tests (no game install needed)
    ./tools.ps1 package [-NoZip]   build + stage dist/NGUAdvisor-<ver>/ + zip dist/dist_<ver>.zip
    ./tools.ps1 reload             unload the live advisor, build, inject, verify the build tag
    ./tools.ps1 reload -UnloadOnly just unload
    ./tools.ps1 release [-DryRun]  CHANGELOG [Unreleased] -> version, commit, tag, GitHub release
    ./tools.ps1 release -Version 2.0.0   override the computed version (major bumps)

    Release version: an [Unreleased] section with only "### Fixed" (or "### Security") bumps the
    patch, anything else bumps the minor. Rerunning a release whose tag already sits on HEAD only
    finishes publishing (push, upload, verify).

    Maintainer machine only: the build needs a local NGU Idle install (see BUILD.md) and the
    git-ignored injector tools in tools/injector (or $env:NGU_TOOLS).
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('build', 'test', 'package', 'reload', 'release')]
    [string]$Command = 'build',
    [switch]$NoZip,
    [switch]$UnloadOnly,
    [switch]$DryRun,
    [string]$Version
)

$ErrorActionPreference = 'Stop'

# This fork, NOT the upstream it was taken from. build/update.ps1 hard-codes the same repo.
$Repo = 'VagnerVit/NGUAdvisor'

$Root          = $PSScriptRoot
$Csproj        = Join-Path $Root 'NGUAdvisor/NGUAdvisor.csproj'
$TestProject   = Join-Path $Root 'tests/NGUAdvisor.Tests'
$Changelog     = Join-Path $Root 'CHANGELOG.md'
$BuildOutput   = Join-Path $Root 'NGUAdvisor/bin/Release/net48'
$Launcher      = Join-Path $Root 'build/Run NGU Advisor.bat'
$Updater       = Join-Path $Root 'build/update.ps1'
$LauncherScript = Join-Path $Root 'build/launch.ps1'
$SampleProfiles = Join-Path $Root 'NGUAdvisor/SampleProfiles'
$Dist          = Join-Path $Root 'dist'
$InjectorTools = if ($env:NGU_TOOLS) { $env:NGU_TOOLS } else { Join-Path $Root 'tools/injector' }
$AdvisorData   = Join-Path $env:USERPROFILE 'AppData/LocalLow/NGUAdvisor'
$DebugLog      = Join-Path $AdvisorData 'logs/debug.log'

$PatchOnlyHeadings = @('Fixed', 'Security')

function Invoke-Native([string]$File, [string[]]$Arguments) {
    & $File @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$File $($Arguments -join ' ') failed (exit $LASTEXITCODE)" }
}

function Write-Step([string]$Text) { Write-Host "==> $Text" -ForegroundColor Cyan }

# ---------------------------------------------------------------- build / test / package

function Get-ProjectVersion {
    $match = [regex]::Match([IO.File]::ReadAllText($Csproj), '<Version>([^<]+)</Version>')
    if (-not $match.Success) { throw "no <Version> in $Csproj" }
    return $match.Groups[1].Value
}

function Invoke-Build {
    # Every build stamps a new file name, so the stale ones go first: then exactly one DLL is "the" build.
    Get-ChildItem $BuildOutput -Filter 'NGUAdvisor.r*.dll' -ErrorAction SilentlyContinue | Remove-Item -Force
    Write-Step 'Building NGUAdvisor (Release)'
    Invoke-Native dotnet @('build', $Csproj, '-c', 'Release', '-v', 'quiet', '-nologo')
    $dll = @(Get-ChildItem $BuildOutput -Filter 'NGUAdvisor.r*.dll')
    if ($dll.Count -ne 1) { throw "expected one NGUAdvisor.r*.dll in $BuildOutput, found $($dll.Count)" }
    Write-Host "    built: $($dll[0].Name)"
    return $dll[0].FullName
}

function Invoke-Tests {
    Write-Step 'Running tests'
    Invoke-Native dotnet @('test', $TestProject, '-nologo', '-v', 'quiet')
}

function Assert-ZipContents([string]$Zip) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Zip)
    try { $entries = $archive.Entries | ForEach-Object { $_.FullName -replace '\\', '/' } }
    finally { $archive.Dispose() }
    $required = @('Run NGU Advisor.bat', 'injector/NGUAdvisor.dll', 'injector/smi.exe',
                  'injector/SharpMonoInjector.dll', 'injector/update.ps1', 'injector/launch.ps1', 'sampleprofiles/')
    foreach ($path in $required) {
        if (-not ($entries | Where-Object { $_ -like "*/$path*" })) { throw "zip is missing $path" }
    }
}

# Returns the zip path, or the stage folder with -NoZip.
function Invoke-Package([switch]$SkipZip) {
    foreach ($tool in 'smi.exe', 'SharpMonoInjector.dll') {
        if (-not (Test-Path (Join-Path $InjectorTools $tool))) {
            throw "missing injector tool $tool in $InjectorTools - place smi.exe and SharpMonoInjector.dll there (BUILD.md) or set NGU_TOOLS"
        }
    }
    $version = Get-ProjectVersion
    $dll = Invoke-Build
    $baked = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll).ProductVersion
    if ($baked -ne $version) { throw "built DLL says version $baked, csproj says $version" }

    $stage = Join-Path $Dist "NGUAdvisor-$version"
    Write-Step "Staging $stage"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    $injectorDir = New-Item -ItemType Directory -Path (Join-Path $stage 'injector') -Force
    Copy-Item $Launcher $stage
    Copy-Item $dll (Join-Path $injectorDir 'NGUAdvisor.dll')
    Copy-Item (Join-Path $InjectorTools 'smi.exe'), (Join-Path $InjectorTools 'SharpMonoInjector.dll') $injectorDir
    # The updater ships next to the DLL it replaces: $PSScriptRoot IS the injector folder.
    Copy-Item $Updater $injectorDir
    Copy-Item $LauncherScript $injectorDir
    Copy-Item $SampleProfiles (Join-Path $stage 'sampleprofiles') -Recurse

    $forbidden = Get-ChildItem $stage -Recurse -File |
        Where-Object { $_.Name -like '*Bootstrap*' -or $_.Name -eq 'Assembly-CSharp.dll' -or $_.Name -like '*.bak*' -or $_.Name -like '*.orig' }
    if ($forbidden) { throw "forbidden file staged: $($forbidden.FullName -join ', ')" }

    if ($SkipZip) { return $stage }

    # The updater searches the archive for injector\NGUAdvisor.dll, but keep the historical layout:
    # everything nested under NGUAdvisor-<version>\.
    $zip = Join-Path $Dist "dist_$version.zip"
    Write-Step "Zipping $zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal
    Assert-ZipContents $zip
    return $zip
}

# ---------------------------------------------------------------- reload into the running game

# NEVER `smi.exe eject`: both forms killed a live game on 2026-08-13 (see the deploying-advisor skill).
# The unload is requested with a file that Main.Update() deletes on the Unity thread once it tore down.
function Invoke-Unload {
    $request = Join-Path $AdvisorData 'unload.request'
    $marker = Join-Path $AdvisorData 'injected.txt'
    $live = $false
    if (Test-Path $marker) {
        $pidLine = (Get-Content $marker | Where-Object { $_ -like 'pid=*' }) -replace 'pid=', ''
        $live = [bool](Get-Process -Id ([int]$pidLine) -ErrorAction SilentlyContinue)
    }
    if (-not $live) { Write-Step 'No advisor live in this process'; return }

    Write-Step 'Requesting unload'
    New-Item -ItemType File -Path $request -Force | Out-Null
    for ($waited = 0; (Test-Path $request) -and $waited -lt 15; $waited++) { Start-Sleep -Seconds 1 }
    if (Test-Path $request) {
        Remove-Item $request -Force
        throw 'the advisor never picked up the unload request - click Unload Advisor in Settings, or restart the game'
    }
    Start-Sleep -Seconds 2
    Write-Host '    unloaded'
}

function Invoke-Reload {
    if (-not (Get-Process NGUIdle -ErrorAction SilentlyContinue)) { throw 'NGU Idle is not running' }
    Invoke-Unload
    if ($UnloadOnly) { return }

    # Destroying the host GameObject is deferred and was seen to take minutes: an inject in that window
    # is refused while reporting success. So inject, check the build tag in the log, retry.
    for ($attempt = 1; $attempt -le 4; $attempt++) {
        $stage = Invoke-Package -SkipZip
        $stamp = [regex]::Match((Get-ChildItem $BuildOutput -Filter 'NGUAdvisor.r*.dll').Name, 'r(\d{6})(\d{4})')
        $wanted = "$($stamp.Groups[1].Value)-$($stamp.Groups[2].Value)"

        Write-Step "Injecting build $wanted"
        Push-Location $stage
        try { Invoke-Native './injector/smi.exe' @('inject', '-p', 'NGUIdle', '-a', './injector/NGUAdvisor.dll', '-n', 'NGUAdvisor', '-c', 'Loader', '-m', 'Init') }
        finally { Pop-Location }

        Start-Sleep -Seconds 6
        # The LAST line: debug.log is appended to, so the first one belongs to whatever build started it.
        $alive = (Select-String -Path $DebugLog -Pattern 'writer alive' | Select-Object -Last 1).Line
        if ($alive -match [regex]::Escape($wanted)) { Write-Step "Live: $alive"; return }
        Write-Host "    attempt ${attempt}: expected build $wanted, log says: $alive"
        if ($attempt -lt 4) { Write-Host '    host object not released yet - retrying in 20 s'; Start-Sleep -Seconds 20 }
    }
    throw 'the new build never took over - restart NGU Idle and inject again'
}

# ---------------------------------------------------------------- release

# Body lines of the "## [<Name>]" section, up to the next "## [" heading.
function Get-ChangelogSection([string[]]$Lines, [string]$Name) {
    $heading = "^## \[$([regex]::Escape($Name))\]"
    $start = 0
    while ($start -lt $Lines.Count -and $Lines[$start] -notmatch $heading) { $start++ }
    if ($start -ge $Lines.Count) { return $null }
    $end = $start + 1
    while ($end -lt $Lines.Count -and $Lines[$end] -notmatch '^## \[') { $end++ }
    $body = if ($end -gt $start + 1) { @($Lines[($start + 1)..($end - 1)]) } else { @() }
    return [pscustomobject]@{ Start = $start; End = $end; Body = $body }
}

function Get-NextVersion([string]$Current, [string[]]$UnreleasedBody) {
    $parsed = [version]$Current
    $headings = $UnreleasedBody | Where-Object { $_ -match '^### ' } | ForEach-Object { ($_ -replace '^### ', '').Trim() }
    $patchOnly = -not ($headings | Where-Object { $_ -notin $PatchOnlyHeadings })
    if ($patchOnly) { return "$($parsed.Major).$($parsed.Minor).$([Math]::Max($parsed.Build, 0) + 1)" }
    return "$($parsed.Major).$($parsed.Minor + 1).0"
}

function Write-TextFile([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding($false)))
}

function Set-ReleaseFiles([string]$NewVersion, [string[]]$Lines, $Unreleased) {
    $today = Get-Date -Format 'yyyy-MM-dd'
    $before = if ($Unreleased.Start -gt 0) { $Lines[0..($Unreleased.Start - 1)] } else { @() }
    $after = if ($Unreleased.End -lt $Lines.Count) { $Lines[$Unreleased.End..($Lines.Count - 1)] } else { @() }
    $body = @($Unreleased.Body)
    while ($body.Count -gt 0 -and -not $body[0].Trim()) { $body = @($body | Select-Object -Skip 1) }
    $updated = @($before) + '## [Unreleased]' + '' + "## [$NewVersion] - $today" + '' + $body + @($after)
    Write-TextFile $Changelog (($updated -join "`n") + "`n")

    $project = [IO.File]::ReadAllText($Csproj)
    Write-TextFile $Csproj ([regex]::Replace($project, '<Version>[^<]+</Version>', "<Version>$NewVersion</Version>"))
}

function Assert-ReadyToRelease {
    $branch = (git rev-parse --abbrev-ref HEAD).Trim()
    if ($branch -ne 'main') { throw "release from main only (on $branch)" }
    if (git status --porcelain) {
        if ($DryRun) { Write-Warning 'working tree is not clean - a real release would stop here' }
        else { throw 'working tree is not clean - commit or stash first' }
    }
    git merge-base --is-ancestor origin/main HEAD
    if ($LASTEXITCODE -ne 0) { throw 'main is behind or diverged from origin/main - pull first' }
}

function Publish-Release([string]$ReleaseVersion) {
    $tag = "v$ReleaseVersion"
    $zip = Join-Path $Dist "dist_$ReleaseVersion.zip"
    if (-not (Test-Path $zip)) { $zip = Invoke-Package }

    Write-Step "Pushing main and $tag"
    Invoke-Native git @('push', 'origin', 'main')
    Invoke-Native git @('push', 'origin', $tag)

    $section = Get-ChangelogSection (Get-Content $Changelog) $ReleaseVersion
    $notes = Join-Path ([IO.Path]::GetTempPath()) "ngu-advisor-notes-$ReleaseVersion.md"
    Write-TextFile $notes (($section.Body -join "`n").Trim() + "`n")

    gh release view $tag --repo $Repo *> $null
    if ($LASTEXITCODE -eq 0) {
        Write-Step "Release $tag exists - replacing its asset"
        Invoke-Native gh @('release', 'upload', $tag, $zip, '--repo', $Repo, '--clobber')
    } else {
        Write-Step "Creating GitHub release $tag"
        Invoke-Native gh @('release', 'create', $tag, $zip, '--repo', $Repo, '--title', "NGU Advisor $tag", '--notes-file', $notes, '--verify-tag')
    }

    # What the players' updater will see: releases/latest must be this tag with a dist_*.zip asset.
    $latest = @(gh api "repos/$Repo/releases/latest" --jq '.tag_name, .assets[].name')
    if ($LASTEXITCODE -ne 0 -or $latest[0] -ne $tag -or $latest -notcontains "dist_$ReleaseVersion.zip") {
        throw "releases/latest does not show $tag with dist_$ReleaseVersion.zip (got: $($latest -join ', '))"
    }
    Write-Step "Released $tag - https://github.com/$Repo/releases/tag/$tag"
}

function Invoke-Release {
    Invoke-Native git @('fetch', 'origin', '--tags', '--quiet')
    $current = Get-ProjectVersion

    if (git tag --points-at HEAD -l "v$current") {
        Write-Step "v$current is already tagged on HEAD - finishing its publication"
        if (-not $DryRun) { Publish-Release $current }
        return
    }

    Assert-ReadyToRelease
    $lines = [string[]](Get-Content $Changelog)
    $unreleased = Get-ChangelogSection $lines 'Unreleased'
    if (-not $unreleased) { throw 'CHANGELOG.md has no "## [Unreleased]" section' }
    if (-not ($unreleased.Body -join '').Trim()) { throw 'CHANGELOG.md [Unreleased] is empty - nothing to release' }

    $next = if ($Version) { $Version } else { Get-NextVersion $current $unreleased.Body }
    $parsedNext = $next -as [version]
    if (-not $parsedNext -or $parsedNext -le [version]$current) { throw "version '$next' must be x.y.z and greater than $current" }
    if (git tag -l "v$next") { throw "tag v$next already exists" }
    Write-Step "Release $current -> $next"
    Write-Host (($unreleased.Body -join "`n").Trim())

    Invoke-Tests
    if ($DryRun) {
        Invoke-Build | Out-Null
        Write-Step "Dry run: would release v$next (nothing changed)"
        return
    }

    try {
        Set-ReleaseFiles $next $lines $unreleased
        Invoke-Package | Out-Null
    } catch {
        Invoke-Native git @('checkout', '--', $Changelog, $Csproj)
        throw
    }

    Invoke-Native git @('add', '--', $Changelog, $Csproj)
    Invoke-Native git @('commit', '-q', '-m', "Release $next")
    Invoke-Native git @('tag', "v$next")
    Publish-Release $next
}

# ---------------------------------------------------------------- dispatch

Push-Location $Root
try {
    switch ($Command) {
        'build'   { Invoke-Build | Out-Null }
        'test'    { Invoke-Tests }
        'package' { $out = Invoke-Package -SkipZip:$NoZip; Write-Step "Done: $out" }
        'reload'  { Invoke-Reload }
        'release' { Invoke-Release }
    }
} finally {
    Pop-Location
}
