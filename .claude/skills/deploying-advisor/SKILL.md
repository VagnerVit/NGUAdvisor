---
name: deploying-advisor
description: Use when a code change must run in the live game — deploying, injecting, unloading, reloading, or hot-swapping the advisor DLL, verifying which build is actually running, or reading the UI AUDIT of a fresh build. Also use when an injected change appears to have no effect.
---

# Deploying and unloading the advisor

The advisor is a DLL injected into a running NGU Idle. Building it proves nothing about what the game
is executing: **injecting over a live instance silently keeps the OLD build.** Mono cannot unload an
assembly, and `Loader.Init` refuses to start a second `Main` (it looks for the GameObject
`NGUAdvisorHost`), so the second inject loads and does nothing but log a Unity warning.

Deploying therefore always means: **unload, then inject, then verify the build tag.**

## The one command

```powershell
pwsh build/reload-advisor.ps1              # unload the live build, rebuild, inject
pwsh build/reload-advisor.ps1 -EjectOnly   # just unload
```

It asks the live advisor to unload (see below), waits for the acknowledgement, runs
`./package-release.sh --inject`, and prints the build tag the game came back with.

## Verify — never assume

```bash
grep "writer alive" ~/AppData/LocalLow/NGUAdvisor/logs/debug.log | head -1
```

The first log line carries the build tag (`build 260813-2225`), and the DLL name printed by the build
carries the same stamp (`NGUAdvisor.r260813222528.dll` → `260813-2225`). Different tags mean the
inject did nothing. `%UserProfile%\AppData\LocalLow\NGUAdvisor\injected.txt` answers the same
question from outside the game (it holds the build and the pid; stale if that pid is gone).

Then read the new build's layout report — it is the only automated test of the UI:

```bash
grep "UI AUDIT" ~/AppData/LocalLow/NGUAdvisor/logs/debug.log | tail -25
```

## Unloading without the UI

Drop a file; the advisor unloads itself:

```
New-Item -ItemType File "$env:USERPROFILE\AppData\LocalLow\NGUAdvisor\unload.request"
```

`Main.Update()` polls it once a second **on the Unity thread**, deletes it as the acknowledgement,
and runs `Loader.Unload()`. Wait for the file to vanish — that is the confirmation, and if it never
does, the running build predates this mechanism (then: click *Unload Advisor* in Settings, or restart
the game).

### NEVER `smi.exe eject` — either form kills the game

Both were tried on a live game on 2026-08-13. Both crashed it.

| Attempt | What happens |
|---|---|
| `eject -m Unload` | The call lands on the INJECTOR's thread while `Loader.Unload()` is Unity and WinForms throughout (`CancelInvoke`, `settingsForm.Close()`, `Object.Destroy`). The main-thread rule in CLAUDE.md is not advisory. The eject reported success, the advisor kept logging, and the game died minutes later with nothing in any log. |
| `eject -m RequestUnload` | The method returns immediately, and `eject` then unloads the assembly out from under a MonoBehaviour still executing it. Instant crash. |

`eject` inherently requires a synchronous teardown, and a safe teardown inherently cannot be
synchronous from outside the Unity thread. There is no third variant to try. The delayed crash of the
first form is what makes this expensive: it reads as unrelated, and the player loses everything since
the last save.

## Do not try to click the button

The advisor window is Mono WinForms, which draws every control into ONE HWND. Child controls have no
handle, so `EnumChildWindows` finds nothing and `BM_CLICK`/`SendMessage` cannot reach the button. The
only UI route left is a real mouse click at coordinates read off a screenshot — slow, fragile, and
unnecessary now that the unload request file exists.

## Quick reference

| Task | How |
|---|---|
| Reload a new build | `pwsh build/reload-advisor.ps1` |
| Unload only | `pwsh build/reload-advisor.ps1 -UnloadOnly` |
| Which build is live? | first `writer alive` line in `debug.log`, or `injected.txt` |
| Is it injected at all? | `injected.txt` exists AND its pid is a running NGUIdle |
| Open the advisor window | **F1** in the game window (Unity `Input`, so it needs real `SendInput` to the foreground game — `PostMessage` will not do) |
| Find the window | `EnumWindows` + `GetClassName` matching `Mono.WinForms*`; `FindWindow` by caption does NOT match it |
| Screenshot the window | `PrintWindow`, never `CopyFromScreen`; call `SetProcessDpiAwarenessContext(-4)` first or you capture the top-left quarter |

## Common mistakes

- **Injecting and reporting success.** The injector prints "Injected" whether or not the build took
  over. Always check the build tag afterwards.
- **Reading a stale audit.** `UI AUDIT` lines are appended per panel build; `tail` them AFTER the
  reload and match the timestamp, or you are reading the previous build's verdict.
- **Trusting `UI AUDIT: clean` for text overflow.** It measures with `TextRenderer.MeasureText` while
  Mono draws wider, so clipped labels pass. A screenshot catches what the audit cannot.
- **A black screenshot is an artifact, not a bug** — `PrintWindow` returns black when the window is
  not in the foreground.
- **Calling any advisor method from outside the game.** Everything reachable from `Character`,
  `MonoBehaviour` or a WinForms form is main-thread-only. From an external caller, request; let
  `Main.Update()` do it.
