# Ubuntu usage

Supported target: Ubuntu 22.04/24.04 x86_64.

```sh
chmod +x run-metroid-rescue.sh
./run-metroid-rescue.sh
```

The app is self-contained and does not require a system .NET runtime. Avalonia
still uses standard desktop libraries supplied by Ubuntu. If launch fails, make
sure X11/Wayland and fontconfig are installed.

Use **Set up Linux USB** once to install `udev` rules through `pkexec`, then
disconnect and reconnect the phone in fastboot mode. If the button cannot run,
install `policykit-1` and ensure your user belongs to `plugdev`.

This Linux build was cross-published from Windows and has not been executed in
an Ubuntu environment yet. It must be tested on Ubuntu and a physical Phone (3)
before public release.
