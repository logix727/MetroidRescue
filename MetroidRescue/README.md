# Metroid Rescue

Metroid Rescue is a focused Windows recovery utility for Nothing Phone (3),
codename `metroid`. It only performs write operations after fastboot reports the
connected product as `metroid`.

## Recovery ladder

1. Detect the phone and collect fastboot diagnostics.
2. Reboot cleanly without modifying partitions.
3. Try the alternate A/B slot after a failed OTA.
4. Extract stock boot-chain images from a Metroid full OTA.
5. Restore `boot`, `init_boot`, `dtbo`, `vendor_boot`, and `vbmeta` on the active
   slot while preserving userdata.
6. Factory reset only as an explicitly confirmed last resort.

The published app includes official Android SDK Platform-Tools, Google's Android
USB driver package, and payload-dumper-go. See `THIRD_PARTY_NOTICES.md`.

Firmware downloads: https://nothingarchive.tech/docs/firmware

This community tool is experimental and not affiliated with Nothing.
