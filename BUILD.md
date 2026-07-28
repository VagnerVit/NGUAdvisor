# Building NGU Advisor

## Prerequisites
- **.NET SDK** (9.x is fine) — installed via `winget install Microsoft.DotNet.SDK.9`.
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
dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release
```
Output: `NGUAdvisor/bin/Release/net48/NGUAdvisor.r<timestamp>.dll` — a single self-contained DLL.
The build stamps a unique `.r<timestamp>` name each time; deploy renames it to `NGUAdvisor.dll`.

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

`SettingsForm.dje.resx` is a dead leftover (no code loads it) and is intentionally not embedded.

## Deploy
Copy the built `NGUAdvisor.r<timestamp>.dll` over `injector/NGUAdvisor.dll` in your runnable
folder, keeping the existing `smi.exe` and `SharpMonoInjector.dll`. Then run `Run NGU Advisor.bat`
with NGU Idle open — it injects `NGUAdvisor.dll` directly (`NGUAdvisor.Loader.Init`).

## Reverting the build system
The original legacy (VS-style) project is preserved as `NGUAdvisor/NGUAdvisor.csproj.legacy`.
