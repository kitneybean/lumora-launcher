#!/usr/bin/env bash
# Builds release archives of the launcher: artifacts/Lumora-<version>-<rid>.zip + artifacts/checksums.txt
# Usage: build/publish.sh <version> <owner/repo> [rid ...]
#   build/publish.sh 0.2.0 kitne/lumora-launcher win-x64 linux-x64 osx-x64 osx-arm64
set -euo pipefail

VERSION="${1:?version, e.g. 0.2.0}"
REPO="${2:?GitHub repository for self-update, e.g. owner/lumora-launcher}"
shift 2
RIDS=("$@")
[ ${#RIDS[@]} -eq 0 ] && RIDS=(win-x64 linux-x64 osx-x64 osx-arm64)

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/artifacts"
rm -rf "$OUT"
mkdir -p "$OUT"

for RID in "${RIDS[@]}"; do
  DIR="$OUT/publish/$RID"
  dotnet publish "$ROOT/src/Lumora.Launcher.App/Lumora.Launcher.App.csproj" -c Release -r "$RID" --self-contained \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none \
    -p:Version="$VERSION" -p:LumoraUpdateRepo="$REPO" -o "$DIR"
  rm -f "$DIR"/*.pdb  # native debug symbols from SkiaSharp/HarfBuzz are not needed
  [ -f "$DIR/Lumora" ] && chmod +x "$DIR/Lumora"
  (cd "$DIR" && zip -qr -X "$OUT/Lumora-$VERSION-$RID.zip" .)
done

(cd "$OUT" && sha256sum Lumora-*.zip > checksums.txt)
rm -rf "$OUT/publish"
ls -la "$OUT"
