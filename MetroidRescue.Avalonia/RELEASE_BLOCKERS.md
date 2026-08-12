# Public release blockers

The source and packages build, but these items require resources unavailable in
the current workspace and must not be represented as completed:

- Test on physical Nothing Phone (3) hardware after failed boot-image and partial
  unofficial LOS installations using exact matching original Nothing OS builds.
- Test USB disconnects and interrupted flashes at each partition group.
- Execute the Linux build on Ubuntu 22.04 and 24.04 under X11 and Wayland.
- Confirm the actual Nothing Phone (3) USB vendor/product IDs in Android,
  bootloader and fastbootd modes. The Windows Google driver may not bind to all
  Nothing IDs; use a verified signed Nothing/OEM driver rather than modifying an
  INF, which would invalidate its signature.
- Obtain Authenticode and Linux package-signing credentials. Builds are not
  signed because no signing key is available.
- Independently reproduce and compare release hashes in a clean CI runner.
- Confirm current Nothing Archive URLs and publisher checksum metadata before
  each release.
- Implement and validate a device-specific stock dynamic-partition layout plan
  before re-enabling firmware or logical-partition restoration. Plain image
  flashing is not represented as rebuilding super metadata.
