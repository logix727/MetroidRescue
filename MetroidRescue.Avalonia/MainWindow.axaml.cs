using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MetroidRescue;

namespace MetroidRescue.Avalonia;

public partial class MainWindow : Window
{
    private readonly FastbootService _fastboot;
    private readonly FirmwareService _firmware;
    private readonly RecoveryMonitor _monitor;
    private readonly RescueJournal _journal = new();
    private readonly FirmwareCatalogService _catalog;
    private readonly ProvenanceService _provenance;
    private readonly PreflightService _preflight;
    private readonly RecoveryReportService _reports = new();
    private FastbootDevice? _device;
    private FirmwareInfo? _firmwareInfo;
    private FirmwareCatalogEntry? _catalogSource;
    private string? _otaPath;
    private CancellationTokenSource? _cancel;
    private readonly SemaphoreSlim _operation = new(1, 1);

    public MainWindow()
    {
        InitializeComponent();
        ToolPaths.EnsureExecutableBits();
        _fastboot = new FastbootService(Log);
        _firmware = new FirmwareService(Log);
        _monitor = new RecoveryMonitor(Log);
        _catalog = new FirmwareCatalogService(Log);
        _provenance = new ProvenanceService(_firmware);
        _preflight = new PreflightService(_fastboot, _firmware, Log);
        SetupButton.Content = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "INSTALL WINDOWS DRIVER" : "SET UP LINUX USB";
        Opened += async (_, _) => await ScanAsync();
    }

    private async void Scan_Click(object? sender, RoutedEventArgs e)
    {
        if (_operation.CurrentCount == 0) { await Alert("Wait for the current recovery operation to finish."); return; }
        await ScanAsync();
    }
    private async Task ScanAsync()
    {
        try
        {
            _device = await _fastboot.DetectAsync();
            if (_device is null) { SetDevice("NO FASTBOOT DEVICE", "Follow the Power + Volume Down instructions, then check USB permissions/driver.", false); return; }
            SetDevice(_device.IsMetroid ? "METROID VERIFIED" : "WRONG DEVICE - WRITES BLOCKED", $"Serial {_device.Serial} | slot {_device.Slot} | {(_device.Unlocked ? "unlocked" : "locked/unknown")} | bootloader {_device.Bootloader}", _device.IsMetroid);
        }
        catch (Exception ex) { Log("ERROR: " + ex.Message); }
    }

    private async void Firmware_Click(object? sender, RoutedEventArgs e)
    {
        if (_operation.CurrentCount == 0) { await Alert("Wait for the current recovery operation to finish."); return; }
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Select Metroid full OTA", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Metroid OTA") { Patterns = ["*.zip"] }] });
        if (files.Count == 0) return;
        _otaPath = files[0].TryGetLocalPath();
        if (_otaPath is null) return;
        _catalogSource = null;
        _firmwareInfo = null;
        try
        {
            _firmwareInfo = await _firmware.InspectAsync(_otaPath);
            if (_firmwareInfo.IsIncremental) throw new InvalidOperationException("Incremental OTA rejected. Select a full OTA.");
            FirmwareState.Text = "METROID OTA INSPECTED - CATALOG REQUIRED FOR WRITES";
            FirmwareState.Foreground = Brushes.ForestGreen;
            FirmwareDetails.Text = $"{_firmwareInfo.Name} | build {_firmwareInfo.Build} | SHA-256 {_firmwareInfo.ShortHash}...";
        }
        catch (Exception ex) { FirmwareState.Text = "FIRMWARE REJECTED"; FirmwareState.Foreground = Brushes.Red; FirmwareDetails.Text = ex.Message; Log("ERROR: " + ex.Message); }
    }

    private async void Catalog_Click(object? sender, RoutedEventArgs e)
    {
        if (_operation.CurrentCount == 0) { await Alert("Wait for the current recovery operation to finish."); return; }
        try
        {
            var entries = await _catalog.GetAsync();
            var dialog = new Window { Title = "Nothing Archive full OTA catalog", Width = 650, Height = 470, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var list = new ListBox { ItemsSource = entries, SelectedIndex = 0, MinHeight = 280 };
            var download = new Button { Content = "DOWNLOAD SELECTED FULL OTA", HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right };
            download.Click += async (_, _) =>
            {
                try
                {
                    if (list.SelectedItem is not FirmwareCatalogEntry entry) return;
                    var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save Metroid full OTA", SuggestedFileName = entry.Build + ".zip", DefaultExtension = "zip" });
                    var path = file?.TryGetLocalPath(); if (path is null) return;
                    download.IsEnabled = false;
                    Phase("DOWNLOADING " + entry.Build, 1);
                    await _catalog.DownloadAsync(entry, path, new Progress<double>(value => Dispatcher.UIThread.Post(() => RescueProgress.Value = value)));
                    var info = await _firmware.InspectAsync(path);
                    FirmwareService.VerifyCatalogIdentity(info, entry);
                    await _firmware.VerifyPublishedChecksumAsync(info, entry);
                    _otaPath = path; _catalogSource = entry; _firmwareInfo = info;
                    FirmwareState.Text = "CATALOG FULL OTA VERIFIED"; FirmwareState.Foreground = Brushes.ForestGreen;
                    FirmwareDetails.Text = $"{entry.Build} | Archive.org SHA-1 {(entry.ExpectedSha1 ?? "unavailable")} | local SHA-256 {_firmwareInfo.ShortHash}...";
                    Phase("FIRMWARE READY", 0);
                    dialog.Close();
                }
                catch (Exception ex)
                {
                    _otaPath = null; _catalogSource = null; _firmwareInfo = null;
                    download.IsEnabled = true;
                    FirmwareState.Text = "FIRMWARE REJECTED"; FirmwareState.Foreground = Brushes.Red; FirmwareDetails.Text = ex.Message;
                    Log("ERROR: " + ex.Message);
                    await Alert(ex.Message);
                }
            };
            dialog.Content = new StackPanel { Margin = new global::Avalonia.Thickness(20), Spacing = 12, Children = { new TextBlock { Text = "Maintained Nothing Archive entries classified as full OTA. Archive.org SHA-1/MD5 is verified when published.", TextWrapping = TextWrapping.Wrap }, list, download } };
            await dialog.ShowDialog(this);
        }
        catch (Exception ex) { Log("ERROR: " + ex.Message); await Alert(ex.Message); }
    }

    private async void Auto_Click(object? sender, RoutedEventArgs e)
    {
        if (!await _operation.WaitAsync(0)) { await Alert("Another device operation is already running."); return; }
        try
        {
        await ScanAsync();
        if (_device is null || !_device.IsMetroid) { await Alert("Metroid was not verified. No writes will run."); return; }
        if (_device.Userspace) { await Alert("Start in bootloader Fastboot Mode, not fastbootd."); return; }
        if (!_device.Unlocked) { await Alert("The bootloader is locked or unlock status is unknown. Boot-chain repair cannot write partitions."); return; }
        if (_firmwareInfo is null || _otaPath is null) { await Alert("Select and verify a Metroid full OTA first."); return; }
        if (_catalogSource is null) { await Alert("Automatic writes require an OTA downloaded and verified through the built-in catalog."); return; }
        if (!string.Equals(MatchBuildText.Text?.Trim(), _catalogSource.Build, StringComparison.Ordinal)) { await Alert($"Type the exact original build '{_catalogSource.Build}' to confirm it matches this phone. If you cannot establish that, automatic writes are blocked."); return; }
        var source = _catalogSource;
        var firmware = _firmwareInfo;
        var accepted = await Confirm($"CONSERVATIVE BOOT-CHAIN REPAIR\n\n1. Re-verify product metroid, bootloader mode, and unlocked state\n2. Verify catalog OTA identity and checksums ({_catalogSource.Build})\n3. Extract and verify stock boot-chain images\n4. Restore boot, init_boot, dtbo, recovery, vendor_boot, and vbmeta to slot A\n5. Activate slot A only after every image succeeds\n6. Reboot and monitor ADB/fastboot\n\nUSERDATA WILL BE PRESERVED. Firmware and dynamic logical partitions will NOT be modified. This cannot undo a completed custom ROM installation that replaced logical system partitions, and version matching cannot be proven while Android is unbootable. Continue?");
        if (!accepted) return;

        _cancel = new CancellationTokenSource(); CancelButton.IsVisible = true; AutoButton.IsEnabled = false;
        try { await RunConservativeRestoreAsync(_device, firmware, source, _cancel.Token); }
        catch (OperationCanceledException) { Phase("CANCELLED BETWEEN COMMANDS", RescueProgress.Value); Log("Cancelled safely between commands."); }
        catch (Exception ex) { Phase("RESCUE STOPPED - REVIEW LOG", RescueProgress.Value); Log("ERROR: " + ex.Message); await Alert(ex.Message); }
        finally { CancelButton.IsVisible = false; AutoButton.IsEnabled = true; _cancel.Dispose(); _cancel = null; }
        }
        finally { _operation.Release(); }
    }

    private async Task RunConservativeRestoreAsync(FastbootDevice device, FirmwareInfo firmware, FirmwareCatalogEntry source, CancellationToken token)
    {
        _journal.Record("session", "started", device, firmware, "Conservative slot A boot-chain rescue");
        Phase("VERIFYING CATALOG OTA", 5);
        FirmwareService.VerifyCatalogIdentity(firmware, source);
        await _firmware.VerifyPublishedChecksumAsync(firmware, source, token);
        Phase("EXTRACTING STOCK BOOT CHAIN", 8);
        await _firmware.ExtractAsync(firmware.Path, token);
        var publishedImageHashes = await _catalog.GetPublishedImageHashesAsync(source, token);
        var manifest = await _provenance.CreateAsync(firmware, source, FirmwareService.RescuePartitions, publishedImageHashes, token);
        Phase("RUNNING BATTERY / USB / PARTITION PREFLIGHT", 14);
        device = await _fastboot.VerifyCurrentAsync(device.Serial, token);
        var preflight = await _preflight.RunAsync(device, ConservativeRecoveryPlan.TargetSlot, token);
        token.ThrowIfCancellationRequested();
        CancelButton.IsVisible = false;
        Log("Boot-chain write group started. Voluntary cancellation is disabled until all six images and slot activation complete.");
        foreach (var write in ConservativeRecoveryPlan.Writes) await Flash(device, firmware, manifest, write.Target, write.Image, CancellationToken.None);
        Phase("ACTIVATING VERIFIED SLOT A", 88); await Fb(device, CancellationToken.None, "set_active", "a");
        CancelButton.IsVisible = true;
        Phase("REBOOTING AND MONITORING", 92); await Fb(device, token, "reboot");
        var result = await _monitor.WaitForBootAsync(device.Serial, TimeSpan.FromMinutes(5), p => Dispatcher.UIThread.Post(() => RescueProgress.Value = 92 + p * .08), token);
        if (result == DeviceTransport.Adb || result == DeviceTransport.AdbUnauthorized)
        {
            var outcome = result == DeviceTransport.Adb ? "repaired-adb-online" : "repaired-adb-authorization-pending";
            Phase(result == DeviceTransport.Adb ? "RESCUE COMPLETE - ANDROID ANSWERED ADB" : "RESCUE COMPLETE - ANDROID ONLINE, AUTHORIZE ADB", 100);
            _journal.Record("session", "completed", device, firmware, outcome);
            var report = await _reports.WriteAsync(outcome, device, firmware, false, "Android transport detected after conservative boot-chain restore.", preflight.Warnings);
            await Alert($"Android is online. Recovery report:\n{report}");
            return;
        }
        if (result == DeviceTransport.Fastboot)
        {
            await _reports.WriteAsync("fastboot-returned", device, firmware, false, "Android did not start and phone returned to fastboot.", preflight.Warnings);
            throw new InvalidOperationException("The phone returned to fastboot, so Android did not start. Export the support ZIP before trying a wipe or another firmware build.");
        }
        if (await Confirm("ADB did not answer. This is normal if USB debugging was never enabled or this computer was never authorized.\n\nCan you now see the Nothing OS setup screen, lock screen, or home screen on the phone?"))
        {
            Phase("RESCUE COMPLETE - BOOT CONFIRMED BY USER", 100);
            _journal.Record("session", "completed", device, firmware, "Boot manually confirmed");
            await _reports.WriteAsync("repaired-user-confirmed", device, firmware, false, "Boot confirmed visually because ADB was unavailable.", preflight.Warnings);
            return;
        }
        _journal.Record("session", "incomplete", device, firmware, result.ToString());
        await _reports.WriteAsync("boot-unconfirmed", device, firmware, false, "Neither ADB nor visual confirmation established boot success.", preflight.Warnings);
        throw new InvalidOperationException("Android did not answer ADB. This may mean USB debugging was never authorized, or boot still failed. If the setup screen appears, the phone recovered. Otherwise return to fastboot with Power + Volume Down and export the support ZIP.");
    }

    private async Task Flash(FastbootDevice device, FirmwareInfo firmware, FirmwareProvenance manifest, string target, string image, CancellationToken token)
    {
        var step = "flash:" + target;
        Phase("FLASHING " + target.ToUpperInvariant(), Math.Min(76, RescueProgress.Value + 1));
        await _provenance.VerifyImageAsync(image, manifest, token);
        await Fb(device, token, "flash", target, _firmware.ImagePath(image));
        _journal.Record(step, "completed", device, firmware, image);
    }

    private async Task Fb(FastbootDevice device, CancellationToken token, params string[] args)
    {
        token.ThrowIfCancellationRequested();
        Log("Current fastboot command cannot be cancelled mid-write.");
        var result = await _fastboot.RunRecoveryCommandAsync(device.Serial, args);
        if (!result.Success) throw new InvalidOperationException("fastboot failed: " + string.Join(' ', args));
    }

    private async void Setup_Click(object? sender, RoutedEventArgs e)
    {
        if (_operation.CurrentCount == 0) { await Alert("Wait for the current recovery operation to finish."); return; }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo("pnputil.exe", $"/add-driver \"{ToolPaths.WindowsDriver}\" /install") { UseShellExecute = true, Verb = "runas" }); return;
        }
        var rule = "SUBSYSTEM==\"usb\", ATTR{idVendor}==\"18d1\", MODE=\"0660\", GROUP=\"plugdev\", TAG+=\"uaccess\"";
        var command = $"printf '%s\\n' '{rule}' > /etc/udev/rules.d/51-metroid-rescue.rules && udevadm control --reload-rules && udevadm trigger";
        Process.Start(new ProcessStartInfo("pkexec") { UseShellExecute = false, ArgumentList = { "sh", "-c", command } });
        await Alert("Linux USB rule installation started. Reconnect the phone after authorization.");
    }

    private async void Support_Click(object? sender, RoutedEventArgs e)
    {
        if (_operation.CurrentCount == 0) { await Alert("Wait for the current recovery operation to finish before querying Fastboot for a support bundle."); return; }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export support ZIP", SuggestedFileName = $"MetroidRescue-Support-{DateTime.Now:yyyyMMdd-HHmmss}.zip", DefaultExtension = "zip" });
        var path = file?.TryGetLocalPath(); if (path is null) return;
        await SupportBundle.CreateAsync(path, LogBox.Text ?? "", _fastboot, _journal, _firmwareInfo); await Alert("Support bundle exported:\n" + path);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => _cancel?.Cancel();
    private void Archive_Click(object? sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://nothingarchive.tech/docs/firmware") { UseShellExecute = true });
    private void SetDevice(string state, string details, bool valid) { DeviceState.Text = state; DeviceState.Foreground = valid ? Brushes.ForestGreen : Brushes.Red; DeviceDetails.Text = details; }
    private void Phase(string text, double progress) => Dispatcher.UIThread.Post(() => { PhaseText.Text = text; RescueProgress.Value = progress; Log(text); });
    private void Log(string line) => Dispatcher.UIThread.Post(() => { LogBox.Text += $"[{DateTime.Now:HH:mm:ss}] {line}\n"; LogBox.CaretIndex = LogBox.Text?.Length ?? 0; });

    private async Task<bool> Confirm(string message) => await Message(message, true);
    private async Task Alert(string message) => await Message(message, false);
    private async Task<bool> Message(string message, bool confirm)
    {
        var dialog = new Window { Title = "Metroid Rescue", Width = 620, Height = 390, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.Parse("#F4F4F0")) };
        var result = false; var buttons = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = 10, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right };
        var ok = new Button { Content = confirm ? "CONTINUE" : "OK" }; ok.Click += (_, _) => { result = true; dialog.Close(); }; buttons.Children.Add(ok);
        if (confirm) { var cancel = new Button { Content = "CANCEL" }; cancel.Click += (_, _) => dialog.Close(); buttons.Children.Add(cancel); }
        dialog.Content = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new global::Avalonia.Thickness(24), Children = { new ScrollViewer { Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 15 } }, buttons } };
        Grid.SetRow(buttons, 1); await dialog.ShowDialog(this); return result;
    }
}
