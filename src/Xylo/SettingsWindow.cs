using System.Drawing;
using System.Windows.Forms;

namespace Xylo;

internal sealed partial class IslandApp
{
    private Form? _settingsWindow;

    private void OpenSettingsWindow()
    {
        if (_settingsWindow is { IsDisposed: false }) { _settingsWindow.Activate(); return; }
        ClosePanel();
        var form = new Form
        {
            Text = "Xylo settings", StartPosition = FormStartPosition.CenterScreen,
            MinimumSize = new Size(520, 500), Size = new Size(600, 700),
            BackColor = Color.FromArgb(16, 20, 24), ForeColor = Color.White,
            Font = new Font("Segoe UI", 10), AutoScaleMode = AutoScaleMode.Dpi
        };
        _settingsWindow = form;
        var content = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(24) };
        form.Controls.Add(content);
        void Heading(string text) => content.Controls.Add(new Label { Text = text, AutoSize = false, Size = new Size(485, 38), Font = new Font("Segoe UI Semibold", 15), ForeColor = Color.FromArgb(151, 236, 201), Padding = new Padding(0, 8, 0, 0) });
        void Option(string text, Func<bool> get, Action toggle)
        {
            var box = new CheckBox { Text = text, AutoSize = false, Size = new Size(485, 42), Padding = new Padding(12, 0, 0, 0), BackColor = Color.FromArgb(28, 34, 39), FlatStyle = FlatStyle.Flat, Checked = get(), Margin = new Padding(0, 4, 0, 4) };
            box.Click += (_, _) => { toggle(); box.Checked = get(); InvalidatePanel(); _dirty = true; };
            form.Activated += (_, _) => box.Checked = get();
            content.Controls.Add(box);
        }
        Heading("Appearance");
        Option("Floating island", () => _settings.Detached, TogglePosition);
        Option("Show battery percentage", () => _settings.ShowBatteryPercent, () => { _settings.ShowBatteryPercent = !_settings.ShowBatteryPercent; SaveAppearance(); });
        Option("Start with Windows", () => StartupService.Enabled, () =>
        {
            try { StartupService.SetEnabled(!StartupService.Enabled); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { ShowPrompt("Startup", "Could not change the Windows startup setting."); }
        });
        void Cycle(string label, Func<string> value, Action change)
        {
            var button = new Button { Size = new Size(485, 42), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(28, 34, 39), ForeColor = Color.White, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0), Margin = new Padding(0, 4, 0, 4) };
            void RefreshLabel() => button.Text = label + ": " + value() + "  ?";
            button.Click += (_, _) => { change(); RefreshLabel(); };
            form.Activated += (_, _) => RefreshLabel();
            RefreshLabel(); content.Controls.Add(button);
        }
        Cycle("Size", () => new[] { "Small", "Normal", "Large" }[_settings.SizePreset], () =>
        { _settings.SizePreset = (_settings.SizePreset + 1) % 3; SaveAppearance(); BuildSurface(); UpdateTarget(); });
        Cycle("Display", () => $"{_settings.MonitorIndex + 1} / {GetDisplays().Count}", () =>
        { var displays = GetDisplays(); _settings.MonitorIndex = (Math.Clamp(_settings.MonitorIndex, 0, displays.Count - 1) + 1) % displays.Count; SaveAppearance(); BuildSurface(); UpdateTarget(); });
        Heading("Tabs");
        for (int id = 1; id < TabNames.Length; id++)
        {
            int tab = id;
            Option("Show " + TabNames[tab], () => !_settings.HiddenTabs.Contains(tab), () =>
            {
                if (!_settings.HiddenTabs.Remove(tab)) _settings.HiddenTabs.Add(tab);
                if (_settings.HiddenTabs.Contains(_slide)) { _slide = 0; _settings.DashboardSlide = 0; SyncAgentAnimation(); UpdateTarget(); }
                SaveAppearance();
            });
        }
        Heading("Features");
        Option("Lyrics", () => _settings.LyricsEnabled, ToggleLyrics);
        Option("Weather", () => _settings.WeatherEnabled, ToggleWeather);
        Option("Clipboard history", () => _clipboardListening, ToggleClipboard);
        Option("Notifications", () => _notifications?.Active == true, ToggleNotifications);
        Option("Do not disturb", () => _doNotDisturb, ToggleDnd);
        Option("Battery alerts", () => _settings.BatteryAlertsEnabled, () => { _settings.BatteryAlertsEnabled = !_settings.BatteryAlertsEnabled; SaveAppearance(); });
        Option("Connectivity alerts", () => _settings.ConnectivityAlertsEnabled, ToggleConnectivity);
        Option("Volume display", () => _settings.VolumeHudEnabled, ToggleVolumeHud);
        Option("Brightness display", () => _settings.BrightnessHudEnabled, ToggleBrightnessHud);
        form.FormClosed += (_, _) => _settingsWindow = null;
        form.Show();
    }
}
