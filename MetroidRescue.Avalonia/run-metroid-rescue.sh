#!/bin/sh
set -eu

HERE=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
chmod +x "$HERE/MetroidRescue" \
  "$HERE/Tools/linux-x64/adb" \
  "$HERE/Tools/linux-x64/fastboot" \
  "$HERE/Tools/linux-x64/payload-dumper-go"

if ! command -v pkexec >/dev/null 2>&1; then
  printf '%s\n' 'Warning: pkexec is missing. Install policykit-1 to use automatic udev setup.' >&2
fi

exec "$HERE/MetroidRescue" "$@"
