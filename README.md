# Metroid Rescue

[![CI](https://github.com/logix727/MetroidRescue/actions/workflows/ci.yml/badge.svg)](https://github.com/logix727/MetroidRescue/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/logix727/MetroidRescue)](https://github.com/logix727/MetroidRescue/releases/latest)
[![License: GPL v3](https://img.shields.io/badge/license-GPLv3-blue.svg)](LICENSE)

Metroid Rescue is an experimental recovery utility for the **Nothing Phone (3)**, codename **`metroid`**. It is designed for phones that are bootlooping or stuck at the Nothing logo but can still enter Fastboot Mode.

> [!WARNING]
> Metroid Rescue has not completed comprehensive physical-device validation. Flashing firmware can permanently damage a device or cause data loss. Read the [known release blockers](MetroidRescue.Avalonia/RELEASE_BLOCKERS.md) before use. This project is not affiliated with or endorsed by Nothing Technology Limited.

## Download

### [Download the latest release](https://github.com/logix727/MetroidRescue/releases/latest)

The release ZIP contains:

- `win-x64/` for Windows 11 x64
- `linux-x64/` for Ubuntu 22.04/24.04 x64
- `SHA256SUMS` for verifying every packaged file

Current release: [v0.1.0](https://github.com/logix727/MetroidRescue/releases/tag/v0.1.0)

- [Direct v0.1.0 ZIP download](https://github.com/logix727/MetroidRescue/releases/download/v0.1.0/MetroidRescue-release.zip)
- ZIP SHA-256: `a792c0e552fba3b6b5390b0e865a442e093b35b308beea454665221591075422`

Releases are currently unsigned. Verify the SHA-256 before running them.

Windows PowerShell:

```powershell
(Get-FileHash .\MetroidRescue-release.zip -Algorithm SHA256).Hash.ToLowerInvariant()
```

Linux:

```bash
sha256sum MetroidRescue-release.zip
```

## What It Does

- Bundles ADB, Fastboot, payload extraction tools, and Windows USB driver files
- Refuses partition writes unless Fastboot reports `product: metroid`
- Downloads or accepts a Nothing Phone (3) full OTA
- Verifies published OTA and extracted-image checksums when available
- Restores boot-chain, firmware, and logical partitions
- Rebuilds the stock A/B logical partition layout
- Preserves userdata by default
- Requires explicit confirmation before an optional userdata wipe
- Checks battery, USB stability, disk space, and image/partition sizes
- Resumes interrupted recovery using a hash-verified operation journal
- Exports recovery reports and a support ZIP

Metroid Rescue never automatically unlocks or relocks the bootloader.

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
7. Select or download a Nothing Phone (3) full OTA.
8. Select **Automatic LOS Rescue**, review the plan, and confirm it.
9. Keep the phone connected until the operation finishes.

For Linux permissions and udev setup, read [LINUX.md](MetroidRescue.Avalonia/LINUX.md).

## Validation Status

Automated CI currently verifies:

- All five unit/simulation tests
- Windows x64 self-contained publish
- Linux x64 self-contained publish
- CI artifacts for both platforms

Not yet verified:

- Recovery on physical Nothing Phone (3) hardware across both slots
- Interrupted physical-device flashes
- Runtime behavior on Ubuntu under X11 and Wayland
- Code signing and verified Nothing-specific Windows driver IDs

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
