# Validation Status

Last updated: 2026-08-12

## Verified without hardware

- Thirty-five automated and live tests cover command failures and cancellation, strict OTA metadata identity, incremental rejection, catalog/build/size matching, pinned manifest verification, guard allowlisting, recovery-plan scope, snapshot state, journal behavior, exact ADB/Fastboot transport parsing, and all four reviewed live catalog OTAs.
- Windows and Linux x64 self-contained publishes compile locally and in GitHub Actions.
- GitHub Actions starts the bundled Linux ADB, Fastboot, and payload-dumper-go binaries.
- Bundled Windows ADB and Fastboot start and report Android Platform-Tools 37.0.1; bundled payload-dumper-go displays its command interface.
- The live Nothing Archive catalog exposes four Metroid full OTAs. The newest available full package is `Metroid_B4.1-260414-1846`; newer listed builds are incremental-only.
- Archive.org metadata provides object SHA-1/MD5 values, and Nothing Archive GitHub releases provide per-image SHA-256 manifests for the catalog builds.
- Automatic writes now require agreement between a reviewed release-pinned allowlist, catalog build timestamp, OTA `post-build-incremental`, Archive.org object checksum, pinned manifest SHA-256, and all requested image hashes.
- Every destructive command re-detects the selected serial and requires exact product `metroid`, bootloader Fastboot mode, and authoritative `unlocked: yes` immediately before execution.
- Preflight blocks writes when Fastboot reports an active Virtual A/B snapshot update or merge.
- The automatic plan writes only the six stock boot-chain images to explicit slot-A targets and activates slot A only after all images succeed.
- Automatic firmware, logical-partition, wipe, unlock, and relock operations are absent.

## Not verified

- Any write on physical Nothing Phone (3) hardware.
- Recovery success for a real LineageOS bootloop or stuck-logo state.
- On-device partition contents after flashing.
- USB disconnect behavior during physical writes.
- GUI operation on Ubuntu X11 or Wayland.
- Nothing-specific Windows USB IDs and driver binding.
- Code signing.

The application remains experimental until the physical-hardware items are completed. See `MetroidRescue.Avalonia/RELEASE_BLOCKERS.md`.

Boot-chain repair is not represented as a full custom-ROM rollback. A completed custom ROM may require restoring dynamic logical partitions through an OEM-authoritative procedure that is not currently available.
