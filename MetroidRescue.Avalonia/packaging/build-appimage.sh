#!/bin/sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
APPDIR="$ROOT/dist/AppDir"
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications"
cp -a "$ROOT/dist/linux-x64/." "$APPDIR/usr/bin/"
cp "$ROOT/packaging/metroid-rescue.desktop" "$APPDIR/usr/share/applications/"
cp "$ROOT/packaging/AppRun" "$APPDIR/AppRun"
chmod +x "$APPDIR/AppRun" "$APPDIR/usr/bin/MetroidRescue" "$APPDIR/usr/bin/Tools/linux-x64/"*
ln -s usr/share/applications/metroid-rescue.desktop "$APPDIR/metroid-rescue.desktop"
if ! command -v appimagetool >/dev/null 2>&1; then
  echo "appimagetool is required to create the final AppImage." >&2
  exit 1
fi
ARCH=x86_64 appimagetool "$APPDIR" "$ROOT/dist/MetroidRescue-x86_64.AppImage"
