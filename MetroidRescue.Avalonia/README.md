# Metroid Rescue

Linux-first recovery utility for Nothing Phone (3), codename `metroid`, with a
secondary Windows 11 build. The UI is Avalonia on .NET 8; the recovery engine is
shared C# and invokes bundled official Android platform-tools.

## Enter fastboot

1. Disconnect USB.
2. Hold Power + Volume Down.
3. At the Nothing logo, release Power while continuing to hold Volume Down.
4. Connect USB and click **Verify Phone**.

Every `adb` and `fastboot` command and response is displayed live. Partition
writes are blocked unless fastboot reports `product: metroid` and an unlocked
bootloader.

## LOS bootloop recovery

Automatic LOS Rescue requires a Nothing Phone (3) full OTA. It verifies the OTA,
computes SHA-256, checks disk space, extracts complete stock partitions, restores
slot A, enters fastbootd for logical partitions, and reboots. Userdata is kept
unless the destructive wipe checkbox is explicitly selected.

This is not guaranteed until tested on physical hardware. Firmware version
matching cannot always be determined when Android does not boot.

Build outputs:

- `dist/linux-x64`: Ubuntu 22.04/24.04 x86_64 target. See `LINUX.md`.
- `dist/win-x64`: Windows 11 x64 target with bundled Google USB driver.
