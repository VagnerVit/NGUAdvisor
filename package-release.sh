#!/usr/bin/env bash
#
# package-release.sh — build a runnable NGU Advisor release zip from THIS (public) source tree.
#
# Produces  dist/dist_<version>.zip  containing:
#   Run NGU Advisor.bat        (single direct-inject launcher — no bootstrap / hot-reload)
#   injector/NGUAdvisor.dll     (freshly built from this tree)
#   injector/SharpMonoInjector.dll, injector/smi.exe   (third-party injector tools)
#   sampleprofiles/             (Normal / Evil / Sadistic)
#
# The DLL is built from the PUBLIC source on purpose: it has no Reload button and no
# bootstrap, so what ships matches what people can build here. Run this on the maintainer's
# machine, where the game assemblies (csproj HintPath) and injector tools exist.
#
# Usage:
#   ./package-release.sh                 # version read from NGUAdvisor/Main.cs
#   ./package-release.sh 1.0.1           # explicit version
#   NGU_RUNTIME=/path/to/NGU ./package-release.sh 1.0.1
#
# After it runs, publish with the printed gh commands.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CSPROJ="$ROOT/NGUAdvisor/NGUAdvisor.csproj"
# This fork, NOT the upstream it was taken from — releases of this tree belong here. Kept as a
# constant rather than read from `git remote` so a clone with a differently-named remote still
# prints a publishable command.
REPO="VagnerVit/NGUAdvisor"

# Flags. --inject skips zipping and injects the freshly built DLL into the running game: the
# inner loop when iterating on UI, where a zip nobody opens is pure overhead.
DO_ZIP=1
DO_INJECT=0
ARGS=()
for a in "$@"; do
  case "$a" in
    --inject) DO_INJECT=1; DO_ZIP=0 ;;
    --no-zip) DO_ZIP=0 ;;
    *) ARGS+=("$a") ;;
  esac
done
set -- ${ARGS[@]+"${ARGS[@]}"}

# Sample profiles ship FROM THE SOURCE TREE — they are ours and they are versioned, so there is
# nothing to locate and nothing to keep in sync by hand.
PROFILES="${NGU_PROFILES:-$ROOT/NGUAdvisor/SampleProfiles}"

# Injector tools are third-party binaries that are NOT in this tree (see .gitignore / BUILD.md).
# Resolution order, first hit wins, so a normal run needs no environment at all:
#   1. NGU_TOOLS               explicit override
#   2. $ROOT/tools/injector    the stable local home — deliberately NOT under dist/, which gets
#                              cleaned out; keeping them in dist/ meant every cleanup broke the
#                              packager and the next release had to hunt for smi.exe
#   3. $RUNTIME/injector       the historical sibling-folder layout
#   4. newest dist/*/injector  last resort: recover from a previously packaged release
RUNTIME="${NGU_RUNTIME:-$ROOT/../NGU}"
if [ -n "${NGU_TOOLS:-}" ]; then
  TOOLS="$NGU_TOOLS"
elif [ -f "$ROOT/tools/injector/smi.exe" ]; then
  TOOLS="$ROOT/tools/injector"
elif [ -f "$RUNTIME/injector/smi.exe" ]; then
  TOOLS="$RUNTIME/injector"
else
  TOOLS="$(ls -td "$ROOT/dist/"*/injector 2>/dev/null | head -1 || true)"
  TOOLS="${TOOLS:-$RUNTIME/injector}"
fi

# Version: first arg, else the Version const in Main.cs.
VERSION="${1:-$(grep -oE 'Version = "[^"]+"' "$ROOT/NGUAdvisor/Main.cs" | head -1 | sed -E 's/.*"([^"]+)".*/\1/')}"
[ -n "$VERSION" ] || { echo "ERROR: could not determine version"; exit 1; }

OUT="$ROOT/dist"
STAGE="$OUT/NGUAdvisor-$VERSION"
ZIP="$OUT/dist_$VERSION.zip"

echo "==> NGU Advisor release packager"
echo "    version : $VERSION"
echo "    tools   : $TOOLS"
echo "    profiles: $PROFILES"
if [ "$DO_INJECT" = 1 ]; then echo "    mode    : build + inject (no zip)"; fi

# --- sanity: required tools present -----------------------------------------
for f in "$TOOLS/smi.exe" "$TOOLS/SharpMonoInjector.dll"; do
  [ -f "$f" ] || { echo "ERROR: missing injector tool: $f
       Put smi.exe and SharpMonoInjector.dll in $ROOT/tools/injector (see BUILD.md), or set NGU_TOOLS."; exit 1; }
done
[ -d "$PROFILES" ] || { echo "ERROR: missing sample profiles: $PROFILES (set NGU_PROFILES)"; exit 1; }

# --- build -------------------------------------------------------------------
echo "==> Building NGUAdvisor (Release)..."
dotnet build "$CSPROJ" -c Release -v quiet
DLL="$(ls -t "$ROOT/NGUAdvisor/bin/Release/net48/"NGUAdvisor.r*.dll 2>/dev/null | head -1)"
[ -f "$DLL" ] || { echo "ERROR: build produced no NGUAdvisor.r*.dll"; exit 1; }
echo "    built: $(basename "$DLL")"

# --- stage -------------------------------------------------------------------
echo "==> Staging $STAGE ..."
# Only remove the zip when this run is going to produce one: --inject / --no-zip must not delete a
# perfectly good release archive as a side effect of staging.
rm -rf "$STAGE"
if [ "$DO_ZIP" = 1 ]; then rm -f "$ZIP"; fi
mkdir -p "$STAGE/injector"

# single direct-inject launcher (CRLF line endings for cmd.exe)
printf '@setlocal enableextensions\r\npushd "%%~dp0"\r\n\r\n.\\injector\\smi.exe inject -p NGUIdle -a .\\injector\\NGUAdvisor.dll -n NGUAdvisor -c Loader -m Init\r\n\r\npopd\r\n' \
  > "$STAGE/Run NGU Advisor.bat"

cp "$DLL" "$STAGE/injector/NGUAdvisor.dll"
cp "$TOOLS/SharpMonoInjector.dll" "$TOOLS/smi.exe" "$STAGE/injector/"
cp -r "$PROFILES" "$STAGE/sampleprofiles"

# --- guard: never ship the bootstrap, game assemblies, or backups ------------
if find "$STAGE" \( -iname '*Bootstrap*' -o -iname 'Assembly-CSharp.dll' -o -iname '*.bak*' -o -iname '*.orig' \) | grep -q .; then
  echo "ERROR: forbidden file staged:"; find "$STAGE" \( -iname '*Bootstrap*' -o -iname 'Assembly-CSharp.dll' -o -iname '*.bak*' -o -iname '*.orig' \)
  exit 1
fi

# --- inject into the running game (inner loop) -------------------------------
# The staged folder is already a complete runnable release, so injection runs from it rather than
# duplicating the smi.exe invocation that "Run NGU Advisor.bat" holds.
if [ "$DO_INJECT" = 1 ]; then
  if ! tasklist //FI "IMAGENAME eq NGUIdle.exe" 2>/dev/null | grep -qi NGUIdle.exe; then
    echo "ERROR: NGUIdle.exe is not running — start the game first."; exit 1
  fi
  echo "==> Injecting into NGUIdle ..."
  ( cd "$STAGE" && ./injector/smi.exe inject -p NGUIdle -a ./injector/NGUAdvisor.dll -n NGUAdvisor -c Loader -m Init )
  echo ""
  echo "==> Injected v$VERSION from $STAGE"
  echo "    log: %UserProfile%\\AppData\\LocalLow\\NGUAdvisor\\logs\\debug.log"
  exit 0
fi

# --- zip (Windows-friendly) --------------------------------------------------
if [ "$DO_ZIP" = 1 ]; then
  echo "==> Zipping..."
  STAGE_WIN="$(cygpath -w "$STAGE")"
  ZIP_WIN="$(cygpath -w "$ZIP")"
  powershell.exe -NoProfile -Command "Compress-Archive -Path '$STAGE_WIN' -DestinationPath '$ZIP_WIN' -CompressionLevel Optimal -Force"

  echo ""
  echo "==> Done: $ZIP  ($(du -h "$ZIP" | cut -f1))"
  echo ""
  echo "Publish a NEW release:"
  echo "  gh release create v$VERSION \"$ZIP\" --repo $REPO --title \"NGU Advisor v$VERSION\" --notes-file <notes.md>"
  echo ""
  echo "Or refresh an EXISTING release's asset:"
  echo "  gh release upload v$VERSION \"$ZIP\" --repo $REPO --clobber"
else
  echo ""
  echo "==> Staged (no zip): $STAGE"
fi
