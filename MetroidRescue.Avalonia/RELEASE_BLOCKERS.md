# Public release blockers

The source and packages build, but these items require resources unavailable in
the current workspace and must not be represented as completed:

- Test on physical Nothing Phone (3) hardware after a failed unofficial LOS
  installation, on both slots, with and without userdata wipe.
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
