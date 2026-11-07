using System.Diagnostics;
using System.Drawing;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Forms;

namespace AcHudLauncher;

internal sealed class LauncherSettings
{
    public int Port { get; set; } = 3001;
    public bool AutoStart { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
}

internal sealed class LauncherForm : Form
{
    private readonly string bundleRoot = AppContext.BaseDirectory;
    private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AcHud", "config.json");
    private readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };
    private readonly NotifyIcon trayIcon;
    private readonly Label statusLabel;
    private readonly TextBox logBox;
    private readonly NumericUpDown portField;
    private readonly CheckBox autoStartField;
    private readonly CheckBox closeToTrayField;
    private readonly Button startButton;
    private readonly Button stopButton;
    private readonly Button restartButton;
    private readonly Button homeButton;
    private readonly Button displayButton;
    private readonly Icon appIcon;
    private Process? backendProcess;
    private Process? collectorProcess;
    private CancellationTokenSource? startupCancellation;
    private bool allowExit;
    private bool starting;
    private bool shuttingDown;

    public LauncherForm()
    {
        Text = "AC Display Unit";
        appIcon = LoadAppIcon();
        Icon = appIcon;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(680, 460);
        Size = new Size(760, 560);
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(18, 22, 29);
        ForeColor = Color.White;

        var settings = LoadSettings();
        portField = new NumericUpDown { Minimum = 1024, Maximum = 65535, Value = settings.Port, Width = 110 };
        autoStartField = new CheckBox { Text = "Start services automatically", Checked = settings.AutoStart, AutoSize = true };
        closeToTrayField = new CheckBox { Text = "Close window to system tray", Checked = settings.CloseToTray, AutoSize = true };
        statusLabel = new Label { Text = "Stopped", AutoSize = true, ForeColor = Color.LightGray, Margin = new Padding(0, 7, 0, 0) };
        logBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(10, 13, 18),
            ForeColor = Color.FromArgb(207, 218, 230),
            Font = new Font("Consolas", 9F),
        };

        startButton = MakeButton("Start", StartServicesAsync);
        stopButton = MakeButton("Stop", StopServicesAsync);
        restartButton = MakeButton("Restart", RestartServicesAsync);
        homeButton = MakeButton("Open Home", OpenHome);
        displayButton = MakeButton("Open Display", OpenDisplay);
        stopButton.Enabled = false;
        restartButton.Enabled = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            ColumnCount = 1,
            RowCount = 5,
            BackColor = BackColor,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label
        {
            Text = "AC Display Unit",
            AutoSize = true,
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            ForeColor = Color.FromArgb(188, 232, 105),
            Margin = new Padding(0, 0, 0, 14),
        }, 0, 0);

        var statusPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 0, 0, 14) };
        statusPanel.Controls.Add(new Label { Text = "Status:", AutoSize = true, ForeColor = Color.FromArgb(160, 174, 190), Margin = new Padding(0, 7, 8, 0) });
        statusPanel.Controls.Add(statusLabel);
        statusPanel.Controls.Add(startButton);
        statusPanel.Controls.Add(stopButton);
        statusPanel.Controls.Add(restartButton);
        statusPanel.Controls.Add(homeButton);
        statusPanel.Controls.Add(displayButton);
        layout.Controls.Add(statusPanel, 0, 1);

        var settingsPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 0, 0, 12) };
        settingsPanel.Controls.Add(new Label { Text = "Port", AutoSize = true, ForeColor = Color.FromArgb(160, 174, 190), Margin = new Padding(0, 7, 6, 0) });
        settingsPanel.Controls.Add(portField);
        settingsPanel.Controls.Add(autoStartField);
        settingsPanel.Controls.Add(closeToTrayField);
        settingsPanel.Controls.Add(MakeButton("Save settings", SaveSettings));
        layout.Controls.Add(settingsPanel, 0, 2);

        layout.Controls.Add(new Label
        {
            Text = "Display: http://localhost:" + settings.Port + "   |   LAN: http://<IP-laptop>:" + settings.Port + "/display",
            AutoSize = true,
            ForeColor = Color.FromArgb(139, 154, 172),
            Margin = new Padding(0, 0, 0, 10),
        }, 0, 3);
        layout.Controls.Add(logBox, 0, 4);
        Controls.Add(layout);

        trayIcon = new NotifyIcon
        {
            Icon = appIcon,
            Text = "AC Display Unit",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu(),
        };
        trayIcon.DoubleClick += (_, _) => ShowWindow();

        FormClosing += HandleFormClosing;
        Shown += async (_, _) =>
        {
            AppendLog($"Configuration: {settingsPath}");
            if (settings.AutoStart) await StartServicesAsync();
        };
    }

    private Icon LoadAppIcon()
    {
        var iconPath = Path.Combine(bundleRoot, "assets", "logo.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;
    }

    private Button MakeButton(string text, Func<Task> action)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(28, 36, 47),
            ForeColor = Color.White,
            Margin = new Padding(6, 0, 0, 0),
            Padding = new Padding(9, 3, 9, 3),
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(65, 81, 98);
        button.Click += async (_, _) => await action();
        return button;
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show AC Display Unit", null, (_, _) => ShowWindow());
        menu.Items.Add("Open Home", null, (_, _) => OpenHome());
        menu.Items.Add("Open Display", null, (_, _) => OpenDisplay());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Start", null, async (_, _) => await StartServicesAsync());
        menu.Items.Add("Stop", null, async (_, _) => await StopServicesAsync());
        menu.Items.Add("Restart", null, async (_, _) => await RestartServicesAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) => await ExitApplicationAsync());
        return menu;
    }

    private LauncherSettings LoadSettings()
    {
        try
        {
            if (File.Exists(settingsPath))
            {
                var settings = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(settingsPath));
                if (settings is not null && settings.Port is >= 1024 and <= 65535) return settings;
            }
        }
        catch { }
        return new LauncherSettings();
    }

    private Task SaveSettings()
    {
        var settings = new LauncherSettings { Port = (int)portField.Value, AutoStart = autoStartField.Checked, CloseToTray = closeToTrayField.Checked };
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, jsonOptions));
        AppendLog("Settings saved. Restart services to apply a changed port.");
        return Task.CompletedTask;
    }

    private async Task StartServicesAsync()
    {
        if (starting || shuttingDown || IsRunning(backendProcess)) return;
        starting = true;
        startupCancellation = new CancellationTokenSource();
        var startupToken = startupCancellation.Token;
        SetStatus("Starting backend…", Color.Gold);
        SetButtons(false, false, false);
        try
        {
            var nodePath = Environment.GetEnvironmentVariable("AC_HUD_NODE_PATH") ?? Path.Combine(bundleRoot, "node", "node.exe");
            var backendDirectory = Path.Combine(bundleRoot, "backend");
            var backendScript = Path.Combine(backendDirectory, "src", "index.mjs");
            var collectorPath = Path.Combine(bundleRoot, "collector", "AcTelemetryCollector.exe");
            if (!File.Exists(nodePath) || !File.Exists(backendScript) || !File.Exists(collectorPath)) throw new InvalidOperationException("Bundle is incomplete. Rebuild production bundle.");

            var port = (int)portField.Value;
            backendProcess = StartProcess(nodePath, backendDirectory, new[] { backendScript }, new Dictionary<string, string>
            {
                ["PORT"] = port.ToString(),
                ["AC_HUD_FRONTEND_DIR"] = Path.Combine(bundleRoot, "web"),
            });
            await WaitForBackendAsync($"http://127.0.0.1:{port}/health", startupToken);
            startupToken.ThrowIfCancellationRequested();
            if (shuttingDown) return;

            collectorProcess = StartProcess(collectorPath, Path.Combine(bundleRoot, "collector"), Array.Empty<string>(), new Dictionary<string, string>
            {
                ["AC_HUD_BACKEND_URL"] = $"http://127.0.0.1:{port}/telemetry/ingest",
            });
            SetStatus("Running", Color.FromArgb(151, 232, 105));
            AppendLog("Production services started.");
        }
        catch (OperationCanceledException) when (startupToken.IsCancellationRequested || shuttingDown)
        {
            AppendLog("Startup canceled.");
        }
        catch (Exception error)
        {
            AppendLog($"Startup failed: {error.Message}");
            SetStatus("Error", Color.IndianRed);
            StopProcess(collectorProcess);
            StopProcess(backendProcess);
            collectorProcess = null;
            backendProcess = null;
        }
        finally
        {
            startupCancellation?.Dispose();
            startupCancellation = null;
            starting = false;
            if (!shuttingDown) SetButtons(!IsRunning(backendProcess), IsRunning(backendProcess), IsRunning(backendProcess));
        }
    }

    private async Task RestartServicesAsync()
    {
        await StopServicesAsync();
        await StartServicesAsync();
    }

    private async Task StopServicesAsync()
    {
        startupCancellation?.Cancel();
        var collector = collectorProcess;
        var backend = backendProcess;
        collectorProcess = null;
        backendProcess = null;

        await StopProcessAsync(collector);
        await StopProcessAsync(backend);
        SetStatus("Stopped", Color.LightGray);
        if (!shuttingDown) SetButtons(true, false, false);
        AppendLog("Production services stopped.");
    }

    private async Task ExitApplicationAsync()
    {
        if (shuttingDown) return;
        shuttingDown = true;
        SetButtons(false, false, false);
        SetStatus("Stopping services…", Color.Gold);

        await StopServicesAsync();

        allowExit = true;
        trayIcon.Visible = false;
        trayIcon.Dispose();
        Close();
    }

    private Task OpenDisplay()
    {
        Process.Start(new ProcessStartInfo($"http://localhost:{(int)portField.Value}/display") { UseShellExecute = true });
        return Task.CompletedTask;
    }

    private Task OpenHome()
    {
        Process.Start(new ProcessStartInfo($"http://localhost:{(int)portField.Value}/") { UseShellExecute = true });
        return Task.CompletedTask;
    }

    private void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void HandleFormClosing(object? sender, FormClosingEventArgs args)
    {
        if (allowExit) return;
        if (!closeToTrayField.Checked)
        {
            args.Cancel = true;
            _ = ExitApplicationAsync();
            return;
        }
        args.Cancel = true;
        Hide();
        trayIcon.ShowBalloonTip(1500, "AC Display Unit", "Running in the system tray.", ToolTipIcon.Info);
    }

    private async Task WaitForBackendAsync(string healthUrl, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync(healthUrl, cancellationToken);
                if (response.IsSuccessStatusCode) { AppendLog("Backend health check passed."); return; }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            await Task.Delay(200, cancellationToken);
        }
        throw new InvalidOperationException("Backend did not become ready within 30 seconds.");
    }

    private Process StartProcess(string fileName, string workingDirectory, IEnumerable<string> arguments, IReadOnlyDictionary<string, string> environment)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        foreach (var pair in environment) startInfo.Environment[pair.Key] = pair.Value;
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}");
        process.OutputDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) AppendLog(eventArgs.Data); };
        process.ErrorDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) AppendLog(eventArgs.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => BeginInvoke(() => { if (!starting && IsHandleCreated) SetStatus("Service exited", Color.IndianRed); });
        return process;
    }

    private static bool IsRunning(Process? process)
    {
        try { return process is not null && !process.HasExited; } catch { return false; }
    }

    private static void StopProcess(Process? process)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(2000);
        }
        catch { }
        finally { process.Dispose(); }
    }

    private void SetButtons(bool startEnabled, bool stopEnabled, bool restartEnabled)
    {
        if (IsDisposed) return;
        startButton.Enabled = startEnabled;
        stopButton.Enabled = stopEnabled;
        restartButton.Enabled = restartEnabled;
    }

    private void SetStatus(string text, Color color)
    {
        if (InvokeRequired) { BeginInvoke(() => SetStatus(text, color)); return; }
        statusLabel.Text = text;
        statusLabel.ForeColor = color;
    }

    private void AppendLog(string message)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => AppendLog(message)); return; }
        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            trayIcon.Visible = false;
            trayIcon.Dispose();
            StopProcess(collectorProcess);
            StopProcess(backendProcess);
        }
        base.Dispose(disposing);
    }

    private static async Task StopProcessAsync(Process? process)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { }

                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2000);
                }
            }
        }
        catch { }
        finally { process.Dispose(); }
    }
}
