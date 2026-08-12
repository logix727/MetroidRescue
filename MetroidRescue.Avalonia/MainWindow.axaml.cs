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
    private FirmwareProvenance? _manifest;
    private HashSet<string> _resumeSteps = [];
    private string? _otaPath;
    private CancellationTokenSource? _cancel;

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

    private async void Scan_Click(object? sender, RoutedEventArgs e) => await ScanAsync();
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
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Select Metroid full OTA", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Metroid OTA") { Patterns = ["*.zip"] }] });
        if (files.Count == 0) return;
        _otaPath = files[0].TryGetLocalPath();
        if (_otaPath is null) return;
        try
        {
            _firmwareInfo = await _firmware.InspectAsync(_otaPath);
            if (_firmwareInfo.IsIncremental) throw new InvalidOperationException("Incremental OTA rejected. Select a full OTA.");
            FirmwareState.Text = "METROID FULL OTA VERIFIED";
            FirmwareState.Foreground = Brushes.ForestGreen;
            FirmwareDetails.Text = $"{_firmwareInfo.Name} | build {_firmwareInfo.Build} | SHA-256 {_firmwareInfo.ShortHash}...";
        }
        catch (Exception ex) { FirmwareState.Text = "FIRMWARE REJECTED"; FirmwareState.Foreground = Brushes.Red; FirmwareDetails.Text = ex.Message; Log("ERROR: " + ex.Message); }
    }

    private async void Catalog_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var entries = await _catalog.GetAsync();
            var dialog = new Window { Title = "Nothing Archive full OTA catalog", Width = 650, Height = 470, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var list = new ListBox { ItemsSource = entries, SelectedIndex = 0, MinHeight = 280 };
            var download = new Button { Content = "DOWNLOAD SELECTED FULL OTA", HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right };
            download.Click += async (_, _) =>
            {
                if (list.SelectedItem is not FirmwareCatalogEntry entry) return;
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save Metroid full OTA", SuggestedFileName = entry.Build + ".zip", DefaultExtension = "zip" });
                var path = file?.TryGetLocalPath(); if (path is null) return;
                dialog.Close();
                Phase("DOWNLOADING " + entry.Build, 1);
                await _catalog.DownloadAsync(entry, path, new Progress<double>(value => Dispatcher.UIThread.Post(() => RescueProgress.Value = value)));
                _otaPath = path; _catalogSource = entry;
                _firmwareInfo = await _firmware.InspectAsync(path);
                await _firmware.VerifyPublishedChecksumAsync(_firmwareInfo, entry);
                FirmwareState.Text = "CATALOG FULL OTA VERIFIED"; FirmwareState.Foreground = Brushes.ForestGreen;
                FirmwareDetails.Text = $"{entry.Build} | published SHA-1 {(entry.ExpectedSha1 ?? "unavailable")} | local SHA-256 {_firmwareInfo.ShortHash}...";
                Phase("FIRMWARE READY", 0);
            };
            dialog.Content = new StackPanel { Margin = new global::Avalonia.Thickness(20), Spacing = 12, Children = { new TextBlock { Text = "Maintained Nothing Archive entries classified as full OTA. Archive.org SHA-1/MD5 is verified when published.", TextWrapping = TextWrapping.Wrap }, list, download } };
            await dialog.ShowDialog(this);
        }
        catch (Exception ex) { Log("ERROR: " + ex.Message); await Alert(ex.Message); }
    }

    private async void Auto_Click(object? sender, RoutedEventArgs e)
    {
        await ScanAsync();
        if (_device is null || !_device.IsMetroid) { await Alert("Metroid was not verified. No writes will run."); return; }
        if (!_device.Unlocked) { await Alert("The bootloader is locked or unlock status is unknown. Full stock restoration cannot write partitions."); return; }
        if (_firmwareInfo is null || _otaPath is null) { await Alert("Select and verify a Metroid full OTA first."); return; }
        var wipe = WipeCheck.IsChecked == true;
        var accepted = await Confirm($"PLAN\n\n1. Verify product metroid and unlocked bootloader\n2. Extract complete stock OTA ({_firmwareInfo.Build})\n3. Set active slot A\n4. Restore stock boot + firmware partitions\n5. Enter fastbootd, rebuild the stock A/B logical layout, and restore system/vendor/product\n6. Restore vbmeta partitions\n7. {(wipe ? "ERASE USERDATA AND METADATA" : "PRESERVE USERDATA")}\n8. Reboot and monitor ADB/fastboot\n\nVersion matching cannot be guaranteed while Android is unbootable. If the custom ROM changed the physical super partition size, this automated path may fail safely and require service-level recovery. Continue?");
        if (!accepted) return;

        _cancel = new CancellationTokenSource(); CancelButton.IsVisible = true; AutoButton.IsEnabled = false;
        try { await RunFullRestoreAsync(_device, _firmwareInfo, wipe, _cancel.Token); }
        catch (OperationCanceledException) { Phase("CANCELLED BETWEEN COMMANDS", RescueProgress.Value); Log("Cancelled safely between commands."); }
        catch (Exception ex) { Phase("RESCUE STOPPED - REVIEW LOG", RescueProgress.Value); Log("ERROR: " + ex.Message); await Alert(ex.Message); }
        finally { CancelButton.IsVisible = false; AutoButton.IsEnabled = true; _cancel.Dispose(); _cancel = null; }
    }

    private async Task RunFullRestoreAsync(FastbootDevice device, FirmwareInfo firmware, bool wipe, CancellationToken token)
    {
        _resumeSteps = _journal.ResumableSteps(device.Serial, firmware.Sha256);
        _journal.Record("session", "started", device, firmware, "LOS full stock rescue");
        Phase("EXTRACTING COMPLETE STOCK OTA", 5);
        if (_catalogSource is not null) await _firmware.VerifyPublishedChecksumAsync(firmware, _catalogSource, token);
        await _firmware.ExtractFullAsync(firmware.Path, token);
        var publishedImageHashes = _catalogSource is null ? [] : await _catalog.GetPublishedImageHashesAsync(_catalogSource, token);
        _manifest = await _provenance.CreateAsync(firmware, _catalogSource, FirmwareService.FullRestorePartitions, publishedImageHashes, token);
        if (_resumeSteps.Count > 0) Log($"Interrupted matching session found. { _resumeSteps.Count } completed steps will be hash-verified before resume.");
        Phase("RUNNING BATTERY / USB / PARTITION PREFLIGHT", 14);
        var preflight = await _preflight.RunAsync(device, token);
        Phase("SETTING SLOT A", 18); await Fb(device, token, "set_active", "a");
        foreach (var partition in FirmwareService.BootPartitions) await Flash(device, partition + "_a", partition, token);
        await Flash(device, "vbmeta_a", "vbmeta", token);
        Phase("ENTERING FASTBOOTD", 35); await Fb(device, token, "reboot", "fastboot"); await Fb(device, token, "wait-for-device");
        foreach (var partition in FirmwareService.FirmwarePartitions) await Flash(device, partition + "_a", partition, token);
        foreach (var partition in FirmwareService.LogicalPartitions) await Flash(device, partition, partition, token);
        Phase("RESTORING VERIFIED BOOT METADATA", 78); await Fb(device, token, "reboot", "bootloader"); await Fb(device, token, "wait-for-device");
        foreach (var partition in FirmwareService.OtherVbmetaPartitions) await Flash(device, partition, partition, token);
        if (wipe) { Phase("ERASING USERDATA", 88); await Fb(device, token, "erase", "userdata"); await Fb(device, token, "erase", "metadata"); }
        Phase("REBOOTING AND MONITORING", 92); await Fb(device, token, "reboot");
        var result = await _monitor.WaitForBootAsync(device.Serial, TimeSpan.FromMinutes(5), p => Dispatcher.UIThread.Post(() => RescueProgress.Value = 92 + p * .08), token);
        if (result == DeviceTransport.Adb || result == DeviceTransport.AdbUnauthorized)
        {
            var outcome = result == DeviceTransport.Adb ? "repaired-adb-online" : "repaired-adb-authorization-pending";
            Phase(result == DeviceTransport.Adb ? "RESCUE COMPLETE - ANDROID ANSWERED ADB" : "RESCUE COMPLETE - ANDROID ONLINE, AUTHORIZE ADB", 100);
            _journal.Record("session", "completed", device, firmware, outcome);
            var report = await _reports.WriteAsync(outcome, device, firmware, wipe, "Android transport detected after restore.", preflight.Warnings);
            await Alert($"Android is online. Recovery report:\n{report}");
            return;
        }
        if (result == DeviceTransport.Fastboot)
        {
            await _reports.WriteAsync("fastboot-returned", device, firmware, wipe, "Android did not start and phone returned to fastboot.", preflight.Warnings);
            throw new InvalidOperationException("The phone returned to fastboot, so Android did not start. Export the support ZIP before trying a wipe or another firmware build.");
        }
        if (await Confirm("ADB did not answer. This is normal if USB debugging was never enabled or this computer was never authorized.\n\nCan you now see the Nothing OS setup screen, lock screen, or home screen on the phone?"))
        {
            Phase("RESCUE COMPLETE - BOOT CONFIRMED BY USER", 100);
            _journal.Record("session", "completed", device, firmware, "Boot manually confirmed");
            await _reports.WriteAsync("repaired-user-confirmed", device, firmware, wipe, "Boot confirmed visually because ADB was unavailable.", preflight.Warnings);
            return;
        }
        _journal.Record("session", "incomplete", device, firmware, result.ToString());
        await _reports.WriteAsync("boot-unconfirmed", device, firmware, wipe, "Neither ADB nor visual confirmation established boot success.", preflight.Warnings);
        throw new InvalidOperationException("Android did not answer ADB. This may mean USB debugging was never authorized, or boot still failed. If the setup screen appears, the phone recovered. Otherwise return to fastboot with Power + Volume Down and export the support ZIP.");
    }

    private async Task Flash(FastbootDevice device, string target, string image, CancellationToken token)
    {
        var step = "flash:" + target;
        if (_resumeSteps.Contains(step) && _manifest is not null)
        {
            await _provenance.VerifyImageAsync(image, _manifest, token);
            Log("Resume: verified and skipping already completed " + target);
            return;
        }
        Phase("FLASHING " + target.ToUpperInvariant(), Math.Min(76, RescueProgress.Value + 1));
        if (_manifest is not null) await _provenance.VerifyImageAsync(image, _manifest, token);
        await Fb(device, token, "flash", target, _firmware.ImagePath(image));
        _journal.Record(step, "completed", device, _firmwareInfo);
    }

    private async Task Fb(FastbootDevice device, CancellationToken token, params string[] args)
    {
        token.ThrowIfCancellationRequested();
        await _fastboot.RequireMetroidAsync(device, true);
        Log("Current fastboot command cannot be cancelled mid-write.");
        var result = await _fastboot.RunAsync(["-s", device.Serial, .. args]);
        if (!result.Success) throw new InvalidOperationException("fastboot failed: " + string.Join(' ', args));
    }

    private async void Setup_Click(object? sender, RoutedEventArgs e)
    {
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
