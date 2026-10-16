# Building NGU Advisor

## Prerequisites
- **.NET SDK 9.x** (pinned by `global.json`) — installed via `winget install Microsoft.DotNet.SDK.9`.
  No Visual Studio needed; net48 reference assemblies come from the
  `Microsoft.NETFramework.ReferenceAssemblies` NuGet package.
- **NGU Idle installed** (Unity 2019.4 / Mono). The build locates its `NGUIdle_Data\Managed\` folder
  automatically, checking the usual Steam roots (`C:\Program Files (x86)\Steam`, `C:\Program Files\Steam`,
  and `SteamLibrary` on C:/D:/E:).

  If your install is somewhere else, point the build at it — no need to edit the `.csproj`:

  ```
  dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release -p:NGUManagedDir="X:\path\to\NGU IDLE\NGUIdle_Data\Managed"
  ```

  For a permanent setting, put it in a `Directory.Build.props` next to the solution:

  ```xml
  <Project>
    <PropertyGroup>
      <NGUManagedDir>X:\path\to\NGU IDLE\NGUIdle_Data\Managed</NGUManagedDir>
    </PropertyGroup>
  </Project>
  ```

  When the folder cannot be found the build fails with that as the error, rather than a few hundred
  "type or namespace not found" errors.

## Why net48 (do not "upgrade")
The DLL is injected into NGU Idle's Unity 2019.4 **Mono (.NET 4.x)** runtime and must be a
.NET Framework 4.x assembly. A modern .NET build cannot be loaded by that runtime.

## Build
```
./tools.ps1 build
```
Output: `NGUAdvisor/bin/Release/net48/NGUAdvisor.r<timestamp>.dll` — a single self-contained DLL.
The build stamps a unique `.r<timestamp>` name each time (Mono byte-load dedup and the build tag
read it); packaging renames it to `NGUAdvisor.dll`. Plain
`dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release` works too.

The product version is `<Version>` in `NGUAdvisor.csproj`, the one source; `Main.Version` reads it
back from the assembly. Only `./tools.ps1 release` changes it.

## WinForms resources — important
The `.resx` are **not** compiled by the SDK at build time. The SDK's resource generator emits the
"preserialized" format, which needs `System.Resources.Extensions.dll` at runtime — that assembly
does not exist in the game's Mono domain, so the settings form would crash on open.

Instead we pre-generate **classic** `.resources` (the format Mono reads natively) and embed those.
`SettingsForm.resources` is what actually gets embedded, and it is checked in.

**The build regenerates it for you.** The `ConvertResx` target runs `build/convert-resx.ps1` whenever
`SettingsForm.resx` (or the converter itself) is newer than `SettingsForm.resources`, so editing the
form in a designer and rebuilding is enough — an ordinary build skips the step. You can still run it
by hand:

```
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build/convert-resx.ps1
```

It must run under **Windows PowerShell 5.1** (`powershell.exe`), not `pwsh`/PowerShell 7, because it
relies on .NET Framework's classic `ResXResourceReader`/`ResourceWriter`. On a host without
`powershell.exe` the target warns and the build falls back to the committed `.resources`.

## `tools.ps1` (maintainer machine)

PowerShell 7 (`pwsh`). One entry point for everything:

```
./tools.ps1 build              # Release build
./tools.ps1 test               # unit tests (no game install needed)
./tools.ps1 package            # build + stage dist/NGUAdvisor-<ver>/ + zip dist/dist_<ver>.zip
./tools.ps1 package -NoZip     # build + stage only
./tools.ps1 reload             # unload the live advisor, build, inject, verify the build tag
./tools.ps1 reload -UnloadOnly # just unload
./tools.ps1 release -DryRun    # show the next version and notes, run tests + build, change nothing
./tools.ps1 release            # CHANGELOG -> version bump, commit, tag, push, GitHub release
```

**`tools/injector/` must hold `smi.exe` and `SharpMonoInjector.dll`** (or point `$env:NGU_TOOLS` at
them). They are third-party binaries ([SharpMonoInjector](https://github.com/warbler/SharpMonoInjector))
and are git-ignored, so a fresh clone needs them placed there once. They deliberately do NOT live
under `dist/`, which gets cleaned out between releases.

`reload` is the inner loop: injecting over a live advisor silently keeps the old build, so it asks the
advisor to unload first (a request file, never `smi.exe eject`) and checks `debug.log` for the new
build tag. Details in `.claude/skills/deploying-advisor/SKILL.md`.

### Release

`./tools.ps1 release` takes no arguments:

1. Refuses unless on `main`, the tree is clean, `origin/main` is not ahead and `[Unreleased]` in
   `CHANGELOG.md` has content.
2. Computes the version: only `### Fixed` / `### Security` headings → patch, anything else → minor.
   `-Version x.y.z` overrides (major bumps).
3. Runs the tests, stamps the CHANGELOG section with today's date, bumps `<Version>`, builds and zips.
   A failure here restores both files; nothing is committed.
4. Commits `Release x.y.z`, tags `vx.y.z` on that commit, pushes both, creates the GitHub release with
   the CHANGELOG section as notes and `dist_x.y.z.zip` as the asset.
5. Verifies that `releases/latest` (what the players' updater reads) shows the tag and the zip.

If it fails after tagging (network, `gh`), run it again: a tag already on HEAD only finishes the
publication.

## Deploy by hand
Copy the built `NGUAdvisor.r<timestamp>.dll` over `injector/NGUAdvisor.dll` in your runnable
folder, keeping the existing `smi.exe` and `SharpMonoInjector.dll`. Then run `Run NGU Advisor.bat`
with NGU Idle open — it injects `NGUAdvisor.dll` directly (`NGUAdvisor.Loader.Init`).

### Auto-update

The launcher (source: `build/Run NGU Advisor.bat`) also writes its own folder into
`%LocalLow%\NGUAdvisor\install.txt` and runs `injector\update.ps1 -Apply`, which installs a release
found by an earlier background check. The mechanism is documented in `docs/modules/UpdateChecker.md`;
it always exits 0, so it can never stop the game from getting its advisor.

**An update never replaces `Run NGU Advisor.bat`**: `cmd.exe` reads a batch file as it executes it,
so a launcher change means re-downloading the zip.
