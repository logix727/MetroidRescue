# Metroid Rescue Avalonia

Cross-platform Nothing Phone (3), codename `metroid`, conservative boot-chain recovery UI for Windows 11 x64 and Ubuntu 22.04/24.04 x64.

## Recovery scope

Automatic recovery:

- Requires bootloader Fastboot Mode and authoritative `unlocked: yes`
- Requires a full OTA downloaded through the built-in Nothing Archive catalog
- Verifies Archive.org object checksum, OTA metadata identity, and published image SHA-256 values
- Writes only `boot_a`, `init_boot_a`, `dtbo_a`, `recovery_a`, `vendor_boot_a`, and `vbmeta_a`
- Activates slot A only after all images succeed
- Preserves userdata and metadata
- Does not write firmware or dynamic logical partitions
- Never unlocks or relocks the bootloader

The broader full-stock restore was withdrawn because stock dynamic-partition reconstruction has not been proven on physical Phone (3) hardware.

## Enter Fastboot Mode

1. Disconnect USB.
2. Hold Power + Volume Down.
3. At the Nothing logo, release Power while continuing to hold Volume Down.
4. Connect USB and select **Verify Phone**.

## Build outputs

- `dist/linux-x64`: Ubuntu 22.04/24.04 x64 target. See `LINUX.md`.
- `dist/win-x64`: Windows 11 x64 target with bundled Google USB driver files.

See the repository root `README.md`, `VALIDATION.md`, and `RELEASE_BLOCKERS.md` before use.
