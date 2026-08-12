# Metroid Rescue

[![CI](https://github.com/logix727/MetroidRescue/actions/workflows/ci.yml/badge.svg)](https://github.com/logix727/MetroidRescue/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/logix727/MetroidRescue)](https://github.com/logix727/MetroidRescue/releases/latest)

Experimental Linux-first recovery utility for the Nothing Phone (3), codename `metroid`. Designed to help users recover from bootloops or a stuck Nothing logo when the phone can still enter Fastboot Mode.

> **WARNING:** This is an experimental community utility, not affiliated with or endorsed by Nothing Technology Limited. Flashing firmware always carries risk. See `RELEASE_BLOCKERS.md` for the current testing/signing status.

## Features

- Linux x64 and Windows 11 x64 builds (self-contained, no .NET runtime required)
- Bundled ADB, Fastboot, and payload extraction tools
- Strict `product: metroid` verification before writing
- Automatic stock restoration from a Nothing full OTA
- Restores boot, firmware, and logical partitions; rebuilds the stock A/B logical layout
- Preserves userdata by default; optional explicitly confirmed wipe
- Preflight checks: battery, USB stability, disk space, partition-size validation
- Nothing Archive firmware catalog with OTA and per-image checksum verification
- Interrupted-flash resume support
- Live ADB/Fastboot command display, recovery reports, and support ZIP export

## Repository layout

```
MetroidRescue/          Core services (Fastboot, firmware, preflight, journal, provenance, reports)
MetroidRescue.Avalonia/ Avalonia UI application, packaging assets, bundled tools
MetroidRescue.Tests/    xUnit test suite
```

## Build

Requires the .NET 8 SDK.

```powershell
dotnet publish MetroidRescue.Avalonia/MetroidRescue.Avalonia.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish MetroidRescue.Avalonia/MetroidRescue.Avalonia.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```

Linux packages: see `MetroidRescue.Avalonia/packaging/` for `.deb` and AppImage scripts and the udev rule. See `MetroidRescue.Avalonia/LINUX.md` for runtime setup.

## Download

Use the [latest GitHub release](https://github.com/logix727/MetroidRescue/releases/latest). Release archives contain both `win-x64` and `linux-x64` builds plus `SHA256SUMS`. Releases are currently unsigned; verify the accompanying SHA-256 file before use.

## Test

```powershell
dotnet test MetroidRescue.Tests/MetroidRescue.Tests.csproj -c Release
```

## Usage

1. Enter Fastboot Mode (hold Power + Volume Down).
2. Connect the phone and select **Verify Phone**.
3. Select or download a Nothing Phone (3) full OTA.
4. Select **Automatic LOS Rescue** and confirm the recovery plan.
5. Keep the cable connected until completion.

## Credits

- [Nothing Archive](https://github.com/spike0en/nothing_archive) firmware catalog and checksums
- Android SDK Platform-Tools
- payload-dumper-go

## License

See `MetroidRescue/THIRD_PARTY_NOTICES.md`. The project is derived from [PixelFlasher](https://github.com/badabing2005/PixelFlasher) (GPL-3.0).
