#!/bin/sh
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
VERSION=${VERSION:-0.1.0}
ARCH=amd64
STAGE="$ROOT/dist/deb-stage"
OUT="$ROOT/dist/metroid-rescue_${VERSION}_${ARCH}.deb"
rm -rf "$STAGE"
mkdir -p "$STAGE/DEBIAN" "$STAGE/opt/metroid-rescue" "$STAGE/usr/bin" "$STAGE/usr/share/applications" "$STAGE/lib/udev/rules.d"
cp -a "$ROOT/dist/linux-x64/." "$STAGE/opt/metroid-rescue/"
cp "$ROOT/packaging/metroid-rescue.desktop" "$STAGE/usr/share/applications/"
cp "$ROOT/packaging/51-metroid-rescue.rules" "$STAGE/lib/udev/rules.d/"
ln -s /opt/metroid-rescue/MetroidRescue "$STAGE/usr/bin/metroid-rescue"
cat > "$STAGE/DEBIAN/control" <<EOF
Package: metroid-rescue
Version: $VERSION
Section: utils
Priority: optional
Architecture: $ARCH
Maintainer: Metroid Rescue Community
Depends: libx11-6, libfontconfig1, libice6, libsm6, libxext6, libxrandr2, policykit-1
Description: Nothing Phone (3) fastboot recovery utility
 Linux-first automated stock recovery for the Nothing Phone (3), codename metroid.
EOF
cat > "$STAGE/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
chmod +x /opt/metroid-rescue/MetroidRescue /opt/metroid-rescue/Tools/linux-x64/adb /opt/metroid-rescue/Tools/linux-x64/fastboot /opt/metroid-rescue/Tools/linux-x64/payload-dumper-go
udevadm control --reload-rules || true
udevadm trigger || true
EOF
chmod 755 "$STAGE/DEBIAN/postinst"
dpkg-deb --root-owner-group --build "$STAGE" "$OUT"
printf '%s\n' "$OUT"
