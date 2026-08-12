## Summary

Describe the change and why it is needed.

## Verification

- [ ] `dotnet test MetroidRescue.Tests/MetroidRescue.Tests.csproj -c Release`
- [ ] Windows self-contained publish succeeds
- [ ] Linux self-contained publish succeeds
- [ ] No serial numbers, credentials, firmware files, or user data are included
- [ ] Device-writing changes preserve the strict `product: metroid` guard
- [ ] Destructive actions require explicit user confirmation

## Hardware testing

State the tested phone mode, slot, host OS, and result. Write `Not tested on hardware` when applicable.
