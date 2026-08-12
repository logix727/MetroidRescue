# Contributing

Changes are welcome, particularly reproducible fixes backed by redacted logs. Never include phone serial numbers, credentials, downloaded firmware, or user data.

## Development checks

Run before submitting a pull request:

```powershell
dotnet test MetroidRescue.Tests/MetroidRescue.Tests.csproj -c Release
dotnet publish MetroidRescue.Avalonia/MetroidRescue.Avalonia.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish MetroidRescue.Avalonia/MetroidRescue.Avalonia.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```

## Safety rules

- Keep the strict `product: metroid` check before every write path.
- Do not automatically unlock or relock the bootloader.
- Do not wipe userdata without explicit, informed confirmation.
- Verify firmware and extracted images before flashing.
- Treat physical-device behavior as unverified unless the exact mode and scenario were tested.
