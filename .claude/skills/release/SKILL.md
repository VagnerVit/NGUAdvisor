---
name: release
description: Use when the user asks to release, publish or ship a new NGU Advisor version ("vydej", "udělej release", "release 1.9"). Not for injecting a build into the running game — that is the deploying-advisor skill.
---

# Releasing NGU Advisor

A release is ONE command. Do not bump versions, edit the CHANGELOG heading, zip, tag or call
`gh release` by hand — the script does all of it and verifies the result.

```powershell
pwsh ./tools.ps1 release -DryRun   # optional: shows next version + notes, runs tests + build
pwsh ./tools.ps1 release
```

Run it only when the user asked for a release: it commits, tags and pushes to `main`.

## Before running

- After any change to how the version is read (csproj `<Version>`, `Main.Version`), the live game must
  show it first: `pwsh ./tools.ps1 reload`, then `writer alive (v<version> build …)` in `debug.log`.
  A `v0.0.0` there would make every player's updater reinstall forever.
- `## [Unreleased]` in `CHANGELOG.md` must hold every player-visible change since the last release.
  Compare it with `git log v<last>..HEAD --oneline`; if a user-facing commit has no entry, write the
  entry (player language, bold lead sentence, like the existing ones), commit it, then release.
- The headings decide the number: only `### Fixed` / `### Security` → patch, anything else → minor.
  A major bump needs `-Version x.0.0`, and only when the user asked for it.
- The tree must be clean and `main` up to date with `origin/main`; the script refuses otherwise.

## When it fails

| Where | What to do |
|---|---|
| Before the commit (tests, build, zip) | Files are restored automatically. Fix the cause, run again. |
| After the tag (push, `gh`, verification) | Run `pwsh ./tools.ps1 release` again: a tag on HEAD only finishes publishing. |
| "releases/latest does not show …" | The release exists but is not `latest` (draft/prerelease?) — check `gh release view v<ver> --repo VagnerVit/NGUAdvisor`. |

Always `--repo VagnerVit/NGUAdvisor` with `gh` here: without it `gh` resolves to the upstream repo.

## Report

The release URL the script prints, and the version. Nothing else — the notes are on GitHub.
