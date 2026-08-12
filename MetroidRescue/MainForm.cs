using System.Diagnostics;

namespace MetroidRescue;

internal sealed class MainForm : Form
{
    private static readonly Color Ink = Color.FromArgb(19, 19, 19);
    private static readonly Color Paper = Color.FromArgb(244, 244, 240);
    private static readonly Color Red = Color.FromArgb(210, 35, 42);
    private static readonly Color Green = Color.FromArgb(33, 145, 87);
    private static readonly Color Muted = Color.FromArgb(105, 105, 105);

    private readonly FastbootService _fastboot;
    private readonly FirmwareService _firmware;
    private readonly RecoveryMonitor _monitor;
    private readonly Label _deviceState = new();
    private readonly Label _deviceDetails = new();
    private readonly Label _firmwareState = new();
    private readonly TextBox _log = new();
    private readonly Button _scanButton = ActionButton("SCAN FASTBOOT", false);
    private readonly Button _extractButton = ActionButton("EXTRACT RESCUE IMAGES", false);
    private readonly Button _alternateSlotButton = ActionButton("TRY OTHER SLOT", false);
    private readonly Button _bootChainButton = ActionButton("RESTORE BOOT CHAIN", true);
    private readonly Button _factoryResetButton = ActionButton("FACTORY RESET", true);
    private readonly Button _autoRescueButton = ActionButton("START AUTOMATIC RESCUE", true);
    private readonly Button _cancelAutoButton = ActionButton("CANCEL", false);
    private readonly Label _autoState = new();
    private readonly ProgressBar _autoProgress = new();
    private FastbootDevice? _device;
    private string? _otaPath;
    private bool _busy;
    private CancellationTokenSource? _autoCancellation;

    public MainForm()
    {
        Text = "Metroid Rescue";
        MinimumSize = new Size(980, 720);
        Size = new Size(1160, 860);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Paper;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        _fastboot = new FastbootService(Log);
        _firmware = new FirmwareService(Log);
        _monitor = new RecoveryMonitor(Log);
        BuildUi();
        Shown += async (_, _) => await ScanAsync();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(20) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 114));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        Controls.Add(root);

        root.Controls.Add(BuildHero(), 0, 0);
        root.Controls.Add(BuildStatusStrip(), 0, 1);
        root.Controls.Add(BuildActions(), 0, 2);
        root.Controls.Add(BuildLog(), 0, 3);
    }

    private Control BuildHero()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Ink, Padding = new Padding(24, 17, 24, 17) };
        var title = new Label { Text = "METROID RESCUE", ForeColor = Color.White, AutoSize = true, Font = new Font("Segoe UI", 25, FontStyle.Bold), Location = new Point(24, 16) };
        var sub = new Label { Text = "NOTHING PHONE (3)  /  FASTBOOT RECOVERY WORKBENCH", ForeColor = Color.Silver, AutoSize = true, Font = new Font("Consolas", 10, FontStyle.Bold), Location = new Point(27, 64) };
        var badge = new Label { Text = " FASTBOOT ONLY ", BackColor = Red, ForeColor = Color.White, AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold), Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new Point(930, 24) };
        panel.Resize += (_, _) => badge.Left = panel.ClientSize.Width - badge.Width - 24;
        panel.Controls.AddRange([title, sub, badge]);
        return panel;
    }

    private Control BuildStatusStrip()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(0, 14, 0, 14) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

        var devicePanel = Card();
        devicePanel.Controls.Add(_deviceDetails);
        devicePanel.Controls.Add(_deviceState);
        _deviceState.Text = "NO FASTBOOT DEVICE";
        _deviceState.Font = new Font("Segoe UI", 13, FontStyle.Bold);
        _deviceState.AutoSize = true;
        _deviceState.Location = new Point(16, 13);
        _deviceDetails.Text = "Connect Phone (3) while holding Volume Down.";
        _deviceDetails.ForeColor = Muted;
        _deviceDetails.AutoSize = true;
        _deviceDetails.Location = new Point(18, 45);

        var firmwarePanel = Card();
        _firmwareState.Text = "RESCUE IMAGES NOT LOADED\nSelect a Metroid full OTA before boot-chain repair.";
        _firmwareState.AutoSize = true;
        _firmwareState.Location = new Point(16, 13);
        firmwarePanel.Controls.Add(_firmwareState);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8, 0, 0, 0) };
        _scanButton.Click += async (_, _) => await ScanAsync();
        var driver = ActionButton("INSTALL USB DRIVER", false);
        driver.Click += (_, _) => InstallDriver();
        buttonPanel.Controls.AddRange([_scanButton, driver]);

        grid.Controls.Add(devicePanel, 0, 0);
        grid.Controls.Add(firmwarePanel, 1, 0);
        grid.Controls.Add(buttonPanel, 2, 0);
        return grid;
    }

    private Control BuildActions()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var reboot = RescueCard("01", "REBOOT CLEANLY", "Safest first step. Reboots without writing partitions.", "REBOOT SYSTEM", async () => await RunFastbootAsync(["reboot"], false));
        var slot = RescueCard("02", "TRY THE OTHER SLOT", "Changes the active A/B slot, then reboots. Useful after a failed OTA.", _alternateSlotButton, TryAlternateSlotAsync);
        var extract = RescueCard("03", "LOAD STOCK RESCUE IMAGES", "Select a Nothing Archive Metroid full OTA and extract only boot-chain images.", _extractButton, SelectAndExtractAsync);
        var bootChain = RescueCard("04", "RESTORE STOCK BOOT CHAIN", "Flashes boot, init_boot, dtbo, vendor_boot and vbmeta to the active slot. Userdata is preserved.", _bootChainButton, RestoreBootChainAsync);
        grid.Controls.Add(reboot, 0, 0);
        grid.Controls.Add(slot, 1, 0);
        grid.Controls.Add(extract, 0, 1);
        grid.Controls.Add(bootChain, 1, 1);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 7, 0, 0) };
        _factoryResetButton.Text = "LAST RESORT: FACTORY RESET";
        _factoryResetButton.Click += async (_, _) => await FactoryResetAsync();
        var archive = ActionButton("OPEN NOTHING ARCHIVE", false);
        archive.Click += (_, _) => Process.Start(new ProcessStartInfo("https://nothingarchive.tech/docs/firmware") { UseShellExecute = true });
        footer.Controls.AddRange([_factoryResetButton, archive]);

        var autoPanel = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Ink, Padding = new Padding(16, 11, 16, 11) };
        _autoRescueButton.Text = "START AUTOMATIC RESCUE";
        _autoRescueButton.BackColor = Red;
        _autoRescueButton.Location = new Point(16, 18);
        _autoRescueButton.Click += async (_, _) => await AutoRescueAsync();
        _cancelAutoButton.Location = new Point(238, 18);
        _cancelAutoButton.Visible = false;
        _cancelAutoButton.Click += (_, _) => _autoCancellation?.Cancel();
        _autoState.Text = "Recommended: diagnose, repair stock boot chain, reboot, verify Android, then retry the alternate slot if needed.";
        _autoState.ForeColor = Color.White;
        _autoState.AutoEllipsis = true;
        _autoState.Location = new Point(338, 13);
        _autoState.Size = new Size(700, 23);
        _autoProgress.Location = new Point(338, 42);
        _autoProgress.Size = new Size(700, 12);
        _autoProgress.Style = ProgressBarStyle.Continuous;
        autoPanel.Resize += (_, _) => { _autoState.Width = Math.Max(200, autoPanel.ClientSize.Width - 360); _autoProgress.Width = _autoState.Width; };
        autoPanel.Controls.AddRange([_autoRescueButton, _cancelAutoButton, _autoState, _autoProgress]);

        var container = new Panel { Dock = DockStyle.Fill };
        grid.Top = 82;
        grid.Height = Math.Max(100, container.Height - 128);
        grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        container.Controls.Add(autoPanel);
        container.Controls.Add(grid);
        container.Controls.Add(footer);
        return container;
    }

    private async Task AutoRescueAsync()
    {
        if (_busy) return;
        var device = await RequireDeviceAsync(false);
        if (device is null) return;

        if (device.Unlocked && !_firmware.ImagesReady)
        {
            using var dialog = new OpenFileDialog { Filter = "Metroid full OTA (*.zip)|*.zip", Title = "Automatic rescue needs a Nothing Phone (3) full OTA" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _otaPath = dialog.FileName;
        }

        var plan = device.Unlocked
            ? "Automatic Rescue will preserve userdata. It will extract stock images, repair the active slot's boot chain, reboot, and monitor startup. If Android does not start and fastboot returns, it will repair and try the other slot.\n\nNo factory reset will be performed. Continue?"
            : "The bootloader is locked, so image repair is unavailable. Automatic Rescue can switch to the other existing slot, reboot, and monitor startup.\n\nNo userdata will be erased. Continue?";
        if (!Confirm(plan)) return;

        _autoCancellation = new CancellationTokenSource();
        _cancelAutoButton.Visible = true;
        _autoRescueButton.Enabled = false;
        _autoProgress.Value = 0;
        await BusyAsync(async () =>
        {
            var token = _autoCancellation.Token;
            SetAutoPhase("VERIFYING METROID", 3);
            device = await _fastboot.DetectAsync(token) ?? throw new InvalidOperationException("Phone disappeared from fastboot.");
            await _fastboot.RequireMetroidAsync(device, false);

            if (!device.Unlocked)
            {
                await AutoTrySlotAsync(device, device.Slot == "a" ? "b" : "a", false, token);
                return;
            }

            if (!_firmware.ImagesReady)
            {
                SetAutoPhase("EXTRACTING STOCK RESCUE IMAGES", 8);
                await _firmware.ExtractAsync(_otaPath!, token);
                _firmwareState.Text = $"RESCUE IMAGES READY\n{Path.GetFileName(_otaPath)}";
                _firmwareState.ForeColor = Green;
            }

            var firstSlot = string.IsNullOrWhiteSpace(device.Slot) ? "a" : device.Slot;
            var success = await AutoTrySlotAsync(device, firstSlot, true, token);
            if (success) return;

            SetAutoPhase("FIRST ATTEMPT FAILED; WAITING FOR FASTBOOT", 62);
            var returned = await WaitForFastbootAsync(device.Serial, TimeSpan.FromMinutes(2), token);
            if (returned is null)
                throw new InvalidOperationException("Android did not answer and the phone did not return to fastboot. Hold Power + Volume Down to return to fastboot, then run Automatic Rescue again.");

            var otherSlot = firstSlot == "a" ? "b" : "a";
            success = await AutoTrySlotAsync(returned, otherSlot, true, token);
            if (!success)
                throw new InvalidOperationException("Both non-destructive rescue attempts failed. Factory reset remains manual; consider a complete stock restore.");
        });
        _cancelAutoButton.Visible = false;
        _autoRescueButton.Enabled = true;
        _autoCancellation.Dispose();
        _autoCancellation = null;
    }

    private async Task<bool> AutoTrySlotAsync(FastbootDevice device, string slot, bool repairBootChain, CancellationToken token)
    {
        SetAutoPhase($"PREPARING SLOT {slot.ToUpperInvariant()}", repairBootChain ? 18 : 15);
        await EnsureSuccessAsync(["-s", device.Serial, "set_active", slot], token);
        if (repairBootChain)
        {
            SetAutoPhase($"RESTORING STOCK BOOT CHAIN ON SLOT {slot.ToUpperInvariant()}", 25);
            foreach (var partition in FirmwareService.RescuePartitions)
            {
                token.ThrowIfCancellationRequested();
                await EnsureSuccessAsync(["-s", device.Serial, "flash", $"{partition}_{slot}", _firmware.ImagePath(partition)], token);
            }
        }
        SetAutoPhase("REBOOTING AND MONITORING ANDROID", 45);
        await EnsureSuccessAsync(["-s", device.Serial, "reboot"], token);
        var result = await _monitor.WaitForBootAsync(device.Serial, TimeSpan.FromMinutes(3), value => SetAutoProgress(45 + value / 2), token);
        if (result == DeviceTransport.Adb)
        {
            SetAutoPhase("RESCUE COMPLETE - ANDROID IS ONLINE", 100);
            MessageBox.Show(this, "Android is online through ADB. Automatic rescue completed without erasing userdata.", "Metroid Rescue", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
        return false;
    }

    private async Task<FastbootDevice?> WaitForFastbootAsync(string serial, TimeSpan timeout, CancellationToken token)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < timeout)
        {
            token.ThrowIfCancellationRequested();
            var detected = await _fastboot.DetectAsync(token);
            if (detected?.Serial.Equals(serial, StringComparison.OrdinalIgnoreCase) == true)
            {
                await _fastboot.RequireMetroidAsync(detected, false);
                return detected;
            }
            await Task.Delay(TimeSpan.FromSeconds(3), token);
        }
        return null;
    }

    private void SetAutoPhase(string text, int progress)
    {
        if (InvokeRequired) { BeginInvoke(() => SetAutoPhase(text, progress)); return; }
        _autoState.Text = text;
        SetAutoProgress(progress);
        Log(text);
    }

    private void SetAutoProgress(int progress)
    {
        if (InvokeRequired) { BeginInvoke(() => SetAutoProgress(progress)); return; }
        _autoProgress.Value = Math.Clamp(progress, 0, 100);
    }

    private Control BuildLog()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Ink, Padding = new Padding(14) };
        _log.Dock = DockStyle.Fill;
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.BackColor = Ink;
        _log.ForeColor = Color.FromArgb(215, 215, 215);
        _log.Font = new Font("Consolas", 9.5f);
        _log.BorderStyle = BorderStyle.None;
        panel.Controls.Add(_log);
        return panel;
    }

    private async Task ScanAsync()
    {
        if (_busy) return;
        await BusyAsync(async () =>
        {
            Log("Scanning bundled fastboot...");
            _device = await _fastboot.DetectAsync();
            if (_device is null)
            {
                _deviceState.Text = "NO FASTBOOT DEVICE";
                _deviceState.ForeColor = Red;
                _deviceDetails.Text = "Install the bundled driver, then reconnect in bootloader mode.";
                return;
            }
            _deviceState.Text = _device.IsMetroid ? "METROID VERIFIED" : "UNSUPPORTED DEVICE";
            _deviceState.ForeColor = _device.IsMetroid ? Green : Red;
            _deviceDetails.Text = $"Serial {_device.Serial}  |  Slot {_device.Slot}  |  {(_device.Unlocked ? "Unlocked" : "Locked / unknown")}\nProduct {_device.Product}  |  Bootloader {_device.Bootloader}";
            Log(_device.IsMetroid ? "Write guard passed: product is metroid." : "Write guard active: this is not metroid.");
        });
    }

    private void InstallDriver()
    {
        var inf = Path.Combine(AppContext.BaseDirectory, "usb_driver", "android_winusb.inf");
        if (!File.Exists(inf)) { ShowError("Bundled Google USB driver is missing."); return; }
        if (!Confirm("Install the bundled Google Android USB driver?\n\nWindows will request administrator permission.")) return;
        Process.Start(new ProcessStartInfo("pnputil.exe", $"/add-driver \"{inf}\" /install") { UseShellExecute = true, Verb = "runas" });
    }

    private async Task SelectAndExtractAsync()
    {
        using var dialog = new OpenFileDialog { Filter = "Metroid full OTA (*.zip)|*.zip", Title = "Select Nothing Phone (3) full OTA" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _otaPath = dialog.FileName;
        await BusyAsync(async () =>
        {
            await _firmware.ExtractAsync(_otaPath);
            _firmwareState.Text = $"RESCUE IMAGES READY\n{Path.GetFileName(_otaPath)}";
            _firmwareState.ForeColor = Green;
            Log($"Images stored in {_firmware.OutputDirectory}");
        });
    }

    private async Task TryAlternateSlotAsync()
    {
        var device = await RequireDeviceAsync(false); if (device is null) return;
        var target = device.Slot == "a" ? "b" : "a";
        if (!Confirm($"Switch active slot from {device.Slot} to {target} and reboot?\n\nUse this after a failed OTA. It does not erase userdata.")) return;
        await BusyAsync(async () =>
        {
            await EnsureSuccessAsync(["-s", device.Serial, "set_active", target]);
            await EnsureSuccessAsync(["-s", device.Serial, "reboot"]);
        });
    }

    private async Task RestoreBootChainAsync()
    {
        var device = await RequireDeviceAsync(true); if (device is null) return;
        if (!_firmware.ImagesReady) { ShowError("Load rescue images from a Metroid full OTA first."); return; }
        var slot = string.IsNullOrWhiteSpace(device.Slot) ? "a" : device.Slot;
        if (!Confirm($"Restore the stock boot chain to slot {slot}?\n\nThis writes boot, init_boot, dtbo, vendor_boot and vbmeta. Userdata is preserved. Use images matching the installed Nothing OS version when possible.")) return;
        await BusyAsync(async () =>
        {
            foreach (var partition in FirmwareService.RescuePartitions)
                await EnsureSuccessAsync(["-s", device.Serial, "flash", $"{partition}_{slot}", _firmware.ImagePath(partition)]);
            await EnsureSuccessAsync(["-s", device.Serial, "reboot"]);
        });
    }

    private async Task FactoryResetAsync()
    {
        var device = await RequireDeviceAsync(true); if (device is null) return;
        if (!Confirm("FACTORY RESET deletes all user data and metadata.\n\nThis cannot be undone. Continue?", true)) return;
        await BusyAsync(async () =>
        {
            await EnsureSuccessAsync(["-s", device.Serial, "erase", "userdata"]);
            await EnsureSuccessAsync(["-s", device.Serial, "erase", "metadata"]);
            await EnsureSuccessAsync(["-s", device.Serial, "reboot"]);
        });
    }

    private async Task RunFastbootAsync(string[] args, bool requireUnlocked)
    {
        var device = await RequireDeviceAsync(requireUnlocked); if (device is null) return;
        await BusyAsync(async () => await EnsureSuccessAsync(["-s", device.Serial, .. args]));
    }

    private async Task<FastbootDevice?> RequireDeviceAsync(bool requireUnlocked)
    {
        _device = await _fastboot.DetectAsync();
        if (_device is null) { ShowError("No fastboot device detected."); return null; }
        try { await _fastboot.RequireMetroidAsync(_device, requireUnlocked); return _device; }
        catch (Exception ex) { ShowError(ex.Message); return null; }
    }

    private async Task EnsureSuccessAsync(string[] args, CancellationToken token = default)
    {
        var result = await _fastboot.RunAsync(args, token);
        if (!result.Success) throw new InvalidOperationException($"fastboot failed: {string.Join(' ', args)}");
    }

    private async Task BusyAsync(Func<Task> action)
    {
        _busy = true;
        UseWaitCursor = true;
        try { await action(); }
        catch (OperationCanceledException) { Log("Automatic rescue cancelled safely between commands."); _autoState.Text = "CANCELLED - NO FACTORY RESET WAS PERFORMED"; }
        catch (Exception ex) { Log("ERROR: " + ex.Message); ShowError(ex.Message); _autoState.Text = "RESCUE STOPPED - REVIEW THE LOG"; }
        finally { _busy = false; UseWaitCursor = false; }
    }

    private void Log(string line)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(line)); return; }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
    }

    private bool Confirm(string text, bool danger = false) => MessageBox.Show(this, text, danger ? "Destructive recovery action" : "Confirm recovery action", MessageBoxButtons.YesNo, danger ? MessageBoxIcon.Stop : MessageBoxIcon.Warning) == DialogResult.Yes;
    private void ShowError(string text) => MessageBox.Show(this, text, "Metroid Rescue", MessageBoxButtons.OK, MessageBoxIcon.Error);

    private static Panel Card() => new() { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0, 0, 10, 0), Height = 78 };

    private static Button ActionButton(string text, bool danger)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = danger ? Red : Ink, ForeColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Bold), Margin = new Padding(4) };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private static Control RescueCard(string number, string title, string description, string buttonText, Func<Task> action)
    {
        var button = ActionButton(buttonText, false);
        return RescueCard(number, title, description, button, action);
    }

    private static Control RescueCard(string number, string title, string description, Button button, Func<Task> action)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0, 0, 10, 10), Padding = new Padding(18) };
        var numberLabel = new Label { Text = number, ForeColor = Red, AutoSize = true, Font = new Font("Consolas", 13, FontStyle.Bold), Location = new Point(18, 16) };
        var titleLabel = new Label { Text = title, ForeColor = Ink, AutoSize = true, Font = new Font("Segoe UI", 13, FontStyle.Bold), Location = new Point(58, 14) };
        var descriptionLabel = new Label { Text = description, ForeColor = Muted, AutoEllipsis = true, Size = new Size(390, 42), Location = new Point(59, 45) };
        button.Location = new Point(58, 92);
        button.Click += async (_, _) => await action();
        panel.Controls.AddRange([numberLabel, titleLabel, descriptionLabel, button]);
        return panel;
    }
}
