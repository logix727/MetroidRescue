param([string]$Version = "0.1.0")
$ErrorActionPreference = "Stop"
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$root = $PSScriptRoot
Remove-Item -LiteralPath "$root\dist" -Recurse -Force -ErrorAction SilentlyContinue
& $dotnet test "$root\..\MetroidRescue.Tests\MetroidRescue.Tests.csproj" -c Release
& $dotnet publish "$root\MetroidRescue.Avalonia.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o "$root\dist\win-x64"
& $dotnet publish "$root\MetroidRescue.Avalonia.csproj" -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o "$root\dist\linux-x64"
Copy-Item -LiteralPath "$root\README.md", "$root\RELEASE_BLOCKERS.md" -Destination "$root\dist\win-x64"
Copy-Item -LiteralPath "$root\README.md", "$root\LINUX.md", "$root\RELEASE_BLOCKERS.md", "$root\run-metroid-rescue.sh" -Destination "$root\dist\linux-x64"
$files = Get-ChildItem -LiteralPath "$root\dist" -Recurse -File | Where-Object { $_.FullName -notmatch "deb-stage|AppDir|SHA256SUMS" } | Sort-Object FullName
$lines = foreach ($file in $files) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $relative = $file.FullName.Substring("$root\dist".Length).TrimStart('\').Replace("\", "/")
    "$hash  $relative"
}
[IO.File]::WriteAllLines("$root\dist\SHA256SUMS", $lines)
Write-Host "Release $Version built. Hardware validation and code signing are still required."
