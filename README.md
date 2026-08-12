# Metroid Rescue

[![CI](https://github.com/logix727/MetroidRescue/actions/workflows/ci.yml/badge.svg)](https://github.com/logix727/MetroidRescue/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/logix727/MetroidRescue)](https://github.com/logix727/MetroidRescue/releases/latest)
[![License: GPL v3](https://img.shields.io/badge/license-GPLv3-blue.svg)](LICENSE)

Metroid Rescue is an experimental, conservative boot-chain recovery utility for the **Nothing Phone (3)**, codename **`metroid`**. It targets failed boot-image or partial custom-ROM installation scenarios where the phone can still enter Fastboot Mode.

> [!WARNING]
> Metroid Rescue has not completed comprehensive physical-device validation. Flashing firmware can permanently damage a device or cause data loss. Read the [known release blockers](MetroidRescue.Avalonia/RELEASE_BLOCKERS.md) before use. This project is not affiliated with or endorsed by Nothing Technology Limited.

## Download

### [Download the latest release](https://github.com/logix727/MetroidRescue/releases/latest)

The release ZIP contains:

- `win-x64/` for Windows 11 x64
- `linux-x64/` for Ubuntu 22.04/24.04 x64
- `SHA256SUMS` for verifying every packaged file

Do not use v0.1.0. Its untested full-restore sequence was withdrawn after a static safety audit. Use the latest release only.

Releases are currently unsigned. Verify the SHA-256 before running them.

Windows PowerShell:

```powershell
(Get-FileHash .\MetroidRescue-v0.2.0.zip -Algorithm SHA256).Hash.ToLowerInvariant()
```

Linux:

```bash
sha256sum MetroidRescue-v0.2.0.zip
```

## What It Does

- Bundles ADB, Fastboot, payload extraction tools, and Windows USB driver files
- Refuses partition writes unless Fastboot reports `product: metroid`
- Downloads a Nothing Phone (3) full OTA through the built-in catalog
- Requires a reviewed release-pinned allowlist, matching Archive.org object checksum, OTA metadata identity, and a complete pinned image-manifest hash
- Restores only `boot`, `init_boot`, `dtbo`, `recovery`, `vendor_boot`, and `vbmeta` to slot A
- Does not modify firmware, dynamic logical partitions, userdata, or metadata
- Activates slot A only after all required boot-chain images are written successfully
- Checks battery, USB stability, disk space, and image/partition sizes
- Records a recovery operation journal and replays all writes after an interruption rather than assuming device contents
- Exports recovery reports and a support ZIP

Metroid Rescue never automatically unlocks or relocks the bootloader.

It cannot reliably undo a completed LineageOS/custom-ROM installation that replaced dynamic logical system partitions. Full stock restoration remains disabled until authoritative stock super metadata, firmware slot policy, anti-rollback rules, and physical-device validation are available.

## Requirements

- Nothing Phone (3), codename `metroid`
- Access to bootloader Fastboot Mode
- Unlocked bootloader for partition restoration
- Approximately 15 GB of free disk space
- Reliable USB cable and adequately charged battery
- Windows 11 x64 or Ubuntu 22.04/24.04 x64

## Basic Recovery

1. Disconnect the USB cable.
2. Hold **Power + Volume Down** for approximately 10 seconds.
3. When the Nothing logo appears, release Power but continue holding Volume Down.
4. Wait for Fastboot Mode, then connect the USB cable.
5. Extract the release ZIP and start Metroid Rescue from the appropriate platform folder.
6. Select **Verify Phone** and confirm that the detected product is `metroid`.
7. Download the exact original Nothing OS build for the phone through the built-in catalog. Manually selected files are inspection-only. If the original build cannot be established, do not run automatic repair.
8. Select **Verified Boot-Chain Repair**, review the limitations and plan, and confirm it.
9. Keep the phone connected until the operation finishes.

For Linux permissions and udev setup, read [LINUX.md](MetroidRescue.Avalonia/LINUX.md).

## Validation Status

Automated CI currently verifies:

- Unit, simulation, live-catalog, guard, and packaging tests
- Windows x64 self-contained publish
- Linux x64 self-contained publish
- CI artifacts for both platforms
- Bundled Linux ADB, Fastboot, and payload-dumper-go startup

Not yet verified:

- Recovery on physical Nothing Phone (3) hardware across both slots
- Interrupted physical-device flashes
- Runtime behavior on Ubuntu under X11 and Wayland
- Code signing and verified Nothing-specific Windows driver IDs
- Whether boot-chain-only repair is sufficient for each LineageOS failure mode
- Version compatibility when the installed Android build cannot be identified

See [RELEASE_BLOCKERS.md](MetroidRescue.Avalonia/RELEASE_BLOCKERS.md) for the complete list.

## Support

- [Report a reproducible bug](https://github.com/logix727/MetroidRescue/issues/new/choose)
- [Ask a usage question](https://github.com/logix727/MetroidRescue/discussions)
- [Report a security issue privately](https://github.com/logix727/MetroidRescue/security/advisories/new)

Remove phone serial numbers and other private information before uploading logs or support bundles.

## Build From Source

Install the .NET 8 SDK, then run:

```powershell
dotnet test MetroidRescue.Tests/MetroidRescue.Tests.csproj -c Release
dotnet publish MetroidRescue.Avalonia/MetroidRescue.Avalonia.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
dotnet publish MetroidRescue.Avalonia/MetroidRescue.Avalonia.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
```

Repository layout:

```text
MetroidRescue/          Core recovery services and original Windows UI
MetroidRescue.Avalonia/ Cross-platform Avalonia UI, packaging, and bundled tools
MetroidRescue.Tests/    xUnit test suite
```

See [CONTRIBUTING.md](CONTRIBUTING.md) before submitting changes.

## Credits

- [Nothing Archive](https://github.com/spike0en/nothing_archive)
- [PixelFlasher](https://github.com/badabing2005/PixelFlasher)
- Android SDK Platform-Tools
- payload-dumper-go

## License

Licensed under [GNU GPL v3](LICENSE). Third-party components retain their respective licenses; see [THIRD_PARTY_NOTICES.md](MetroidRescue/THIRD_PARTY_NOTICES.md).
