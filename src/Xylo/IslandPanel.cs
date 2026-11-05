using static Xylo.Native;

namespace Xylo;

internal sealed partial class IslandApp
{
    private const int PanelRowsPerPage = 6;
    private PanelPage? _panel;
    private int _panelPage;
    private TaskItem? _panelTask;
    private PanelRow[] _visiblePanelRows = Array.Empty<PanelRow>();
    private PanelView? _panelView;
    private bool _testPanelPagination;
    private bool _testWeatherToggled;
    private bool _testConnectivityToggled;
    private bool _testVolumeToggled;
    private bool _testBrightnessToggled;
    private bool _testLyricsToggled;
    private bool _testAppearanceToggled, _testSizeChanged, _testTabToggled, _testTabMoved;
    private int _selectedTab;
    private static readonly string[] TabNames = { "Today", "Agent", "Weather", "To-do", "Music", "Clipboard", "Focus" };

    private void ShowMenu()
    {
        if (_composer != null || _review != null || _prompt != null) return;
        if (_panel != null) { ClosePanel(); return; }
        IntPtr foreground = GetForegroundWindow();
        if (foreground != IntPtr.Zero && foreground != _hwnd) _previousForeground = foreground;
        _panel = PanelPage.Home;
        _panelPage = 0;
        InvalidatePanel();
        _menuOpen = true;
        KillTimer(_hwnd, (UIntPtr)CollapseTimer);
        _expanded = true;
        UpdateTarget();
        _dirty = true;
    }

    private void ClosePanel()
    {
        _panel = null;
        _panelTask = null;
        _visiblePanelRows = Array.Empty<PanelRow>();
        _panelView = null;
        _menuOpen = false;
        GetCursorPos(out var cursor);
        _expanded = _composer != null || _review != null || _prompt != null ||
            IsInsidePill((cursor.X - _x) / _scale, (cursor.Y - _y) / _scale);
        UpdateTarget();
        _dirty = true;
    }

    private void NavigatePanel(PanelPage page)
    {
        _panel = page;
        _panelPage = 0;
        InvalidatePanel();
    }

    private PanelView? BuildPanelView()
    {
        if (_panel == null) return null;
        if (_panelView != null) return _panelView;
        var rows = new List<PanelRow>();
        string title;
        switch (_panel.Value)
        {
            case PanelPage.Home:
                title = "Controls";
                rows.Add(new(_settings.Detached ? "Position: Floating" : "Position: Attached", TogglePosition));
                rows.Add(new(_notifications?.Active == true ? "Notifications: On" : "Notifications: Off", ToggleNotifications));
                rows.Add(new(_clipboardListening ? "Clipboard history: On" : "Clipboard history: Off", ToggleClipboard));
                rows.Add(new("Clipboard history  >", () => NavigatePanel(PanelPage.Clipboard)));
                rows.Add(new($"Tasks ({_settings.Tasks.Count})  >", () => NavigatePanel(PanelPage.Tasks)));
                rows.Add(new("Focus timer  >", () => NavigatePanel(PanelPage.Focus)));
                rows.Add(new(_settings.WeatherEnabled ? "Weather: On" : "Weather: Off", ToggleWeather));
                rows.Add(new(_settings.BatteryAlertsEnabled ? "Battery alerts: On" : "Battery alerts: Off", () =>
                {
                    bool previous = _settings.BatteryAlertsEnabled;
                    _settings.BatteryAlertsEnabled = !previous;
                    try { _settings.Save(); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    { _settings.BatteryAlertsEnabled = previous; ShowPrompt("Battery", "Could not save the alert setting."); }
                }));
                rows.Add(new(_settings.ConnectivityAlertsEnabled ? "Connectivity alerts: On" : "Connectivity alerts: Off", ToggleConnectivity));
                rows.Add(new(_settings.VolumeHudEnabled ? "Volume HUD: On" : "Volume HUD: Off", ToggleVolumeHud));
                rows.Add(new(_settings.BrightnessHudEnabled ? "Brightness HUD: On" : "Brightness HUD: Off", ToggleBrightnessHud));
                rows.Add(new(_settings.LyricsEnabled ? "Lyrics: On" : "Lyrics: Off", ToggleLyrics));
                rows.Add(new("Appearance  >", () => NavigatePanel(PanelPage.Appearance)));
                rows.Add(new("Settings window  >", OpenSettingsWindow));
                rows.Add(new("Quit Xylo", () => DestroyWindow(_hwnd)));
                break;
            case PanelPage.Appearance:
                title = "Appearance";
                rows.Add(new(_settings.ShowBatteryPercent ? "Battery percentage: On" : "Battery percentage: Off", () =>
                { _settings.ShowBatteryPercent = !_settings.ShowBatteryPercent; if (_taskTest && !_settings.ShowBatteryPercent) _testAppearanceToggled = true; SaveAppearance(); }));
                rows.Add(new(_settings.Detached ? "Position: Floating" : "Position: Attached", TogglePosition));
                rows.Add(new("Size: " + new[] { "Small", "Normal", "Large" }[_settings.SizePreset], () =>
                { _settings.SizePreset = (_settings.SizePreset + 1) % 3; if (_taskTest) _testSizeChanged = true; SaveAppearance(); BuildSurface(); UpdateTarget(); }));
                var displays = GetDisplays();
                rows.Add(new($"Display: {Math.Clamp(_settings.MonitorIndex, 0, displays.Count - 1) + 1} / {displays.Count}", () =>
                { _settings.MonitorIndex = (Math.Clamp(_settings.MonitorIndex, 0, displays.Count - 1) + 1) % displays.Count; SaveAppearance(); BuildSurface(); UpdateTarget(); }));
                rows.Add(new("Tabs: visibility and order  >", () => NavigatePanel(PanelPage.Tabs)));
                rows.Add(new(StartupService.Enabled ? "Start with Windows: On" : "Start with Windows: Off", () =>
                {
                    try { StartupService.SetEnabled(!StartupService.Enabled); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    { ShowPrompt("Startup", "Could not change the Windows startup setting."); }
                }));
                break;
            case PanelPage.Tabs:
                title = "Arrange tabs";
                foreach (int id in _settings.TabOrder)
                {
                    int tab = id;
                    rows.Add(new((!_settings.HiddenTabs.Contains(tab) ? "[shown] " : "[hidden] ") + TabNames[tab] + "  >", () => { _selectedTab = tab; NavigatePanel(PanelPage.TabDetails); }));
                }
                break;
            case PanelPage.TabDetails:
                title = TabNames[_selectedTab];
                if (_selectedTab == 0) rows.Add(new("Today always remains visible", () => { }));
                else rows.Add(new(_settings.HiddenTabs.Contains(_selectedTab) ? "Show tab" : "Hide tab", () =>
                {
                    if (!_settings.HiddenTabs.Remove(_selectedTab)) _settings.HiddenTabs.Add(_selectedTab);
                    if (_taskTest && _settings.HiddenTabs.Contains(_selectedTab)) _testTabToggled = true;
                    if (_settings.HiddenTabs.Contains(_slide)) { _slide = 0; _settings.DashboardSlide = 0; SyncAgentAnimation(); UpdateTarget(); }
                    SaveAppearance();
                }));
                rows.Add(new("Move left", () => MoveTab(-1)));
                rows.Add(new("Move right", () => MoveTab(1)));
                break;
            case PanelPage.Tasks:
                title = "Tasks";
                rows.Add(new("+ Add task", () =>
                {
                    if (_settings.Tasks.Count >= 30) ShowPrompt("Tasks", "Xylo supports up to 30 tasks.");
                    else { ClosePanel(); BeginComposer(null); }
                }));
                foreach (var item in _settings.Tasks)
                {
                    var task = item;
                    string label = (task.Done ? "[done] " : "[ ] ") + task.Title.Replace('\r', ' ').Replace('\n', ' ');
                    if (task.Due is DateTime due) label += " · " + due.ToString(task.HasTime ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd");
                    rows.Add(new(label, () => { _panelTask = task; NavigatePanel(PanelPage.TaskDetails); }));
                }
                break;
            case PanelPage.TaskDetails:
                title = _panelTask?.Title ?? "Task";
                var selected = _panelTask;
                if (selected != null && _settings.Tasks.Contains(selected))
                {
                    rows.Add(new(selected.Done ? "Mark open" : "Mark done", () => { selected.Done = !selected.Done; SaveTasks(); _dirty = true; }));
                    rows.Add(new("Edit task", () =>
                    {
                        int index = _settings.Tasks.IndexOf(selected);
                        if (index >= 0) { ClosePanel(); BeginComposer(index); }
                    }));
                    rows.Add(new("Delete task", () => ShowPrompt("Delete task?", selected.Title, "Delete", () =>
                    {
                        if (_settings.Tasks.Remove(selected)) SaveTasks();
                        NavigatePanel(PanelPage.Tasks);
                    }, dangerous: true)));
                }
                break;
            case PanelPage.Clipboard:
                title = "Clipboard";
                if (!_clipboardListening) rows.Add(new("Enable clipboard history in Controls", () => NavigatePanel(PanelPage.Home)));
                else
                {
                    rows.Add(new("Clear history", () => { _clipboard.Clear(); _dirty = true; }));
                    foreach (string item in _clipboard.Items.ToArray())
                    {
                        string value = item;
                        string label = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
                        rows.Add(new(label, () =>
                        {
                            if (_clipboard.CopyValue(_hwnd, value)) ClosePanel();
                            else ShowPrompt("Clipboard", "Could not copy the selected item.");
                        }));
                    }
                }
                break;
            case PanelPage.Focus:
                title = _focus.Active ? $"{_focus.Label}: {_focus.Display}" : "Focus timer";
                rows.Add(new("Start focus (25 min)", () => StartFocus("Focus", 25 * 60)));
                rows.Add(new("Start break (5 min)", () => StartFocus("Break", 5 * 60)));
                rows.Add(new("Start long break (15 min)", () => StartFocus("Long break", 15 * 60)));
                rows.Add(new(_focus.Running ? "Pause timer" : "Resume timer", () =>
                {
                    _focus.Toggle();
                    if (_focus.Running) SetTimer(_hwnd, (UIntPtr)FocusTickTimer, 1000, IntPtr.Zero);
                    else KillTimer(_hwnd, (UIntPtr)FocusTickTimer);
                    _dirty = true;
                }));
                rows.Add(new("Clear timer", () => { _focus.Clear(); KillTimer(_hwnd, (UIntPtr)FocusTickTimer); _dirty = true; }));
                break;
            default: return null;
        }
        int pageCount = Math.Max(1, (rows.Count + PanelRowsPerPage - 1) / PanelRowsPerPage);
        _panelPage = Math.Clamp(_panelPage, 0, pageCount - 1);
        _visiblePanelRows = rows.Skip(_panelPage * PanelRowsPerPage).Take(PanelRowsPerPage).ToArray();
        return _panelView = new PanelView(title, _visiblePanelRows.Select(row => row.Label).ToArray(), _panelPage, pageCount, _panel != PanelPage.Home);
    }

    private void HandlePanelClick(float x, float y)
    {
        if (_panel == null || _height.Value < 370) return;
        if (x is >= 24 and <= 376 && y is >= 78 and < 306)
        {
            int index = (int)((y - 78) / 38);
            float within = (y - 78) % 38;
            if (within < 34 && index < _visiblePanelRows.Length) _visiblePanelRows[index].Invoke();
        }
        else if (y is >= 325 and <= 365)
        {
            if (x is >= 24 and <= 100 && _panel != PanelPage.Home)
                NavigatePanel(_panel == PanelPage.TaskDetails ? PanelPage.Tasks : _panel == PanelPage.TabDetails ? PanelPage.Tabs : _panel == PanelPage.Tabs ? PanelPage.Appearance : PanelPage.Home);
            else if (x is >= 108 and <= 174) _panelPage = Math.Max(0, _panelPage - 1);
            else if (x is >= 180 and <= 246) _panelPage++;
            else if (x is >= 260 and <= 376) ClosePanel();
        }
        if (_taskTest && _panelPage > 0) _testPanelPagination = true;
        InvalidatePanel();
    }

    private void InvalidatePanel()
    {
        _panelView = null;
        _visiblePanelRows = Array.Empty<PanelRow>();
        _dirty = true;
    }

    private void TogglePosition()
    {
        bool previous = _settings.Detached;
        _settings.Detached = !previous;
        try { _settings.Save(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { _settings.Detached = previous; ShowPrompt("Configuration", "Could not save Xylo configuration."); }
        _y = _displayBounds.top + (_settings.Detached ? (int)(12 * _scale) : 0);
        _dirty = true;
    }

    private void SaveAppearance()
    {
        try { _settings.Save(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { ShowPrompt("Appearance", "Could not save display settings."); }
        InvalidatePanel();
    }

    private void MoveTab(int direction)
    {
        int index = _settings.TabOrder.IndexOf(_selectedTab), next = index + direction;
        if (index < 0 || next < 0 || next >= _settings.TabOrder.Count) return;
        (_settings.TabOrder[index], _settings.TabOrder[next]) = (_settings.TabOrder[next], _settings.TabOrder[index]);
        if (_taskTest) _testTabMoved = true;
        SaveAppearance();
    }

    private void ToggleNotifications()
    {
        if (_notifications == null || _notifications.Requesting) return;
        if (_notifications.Active)
        {
            _notifications.Stop();
            _settings.NotificationsEnabled = false;
            try { _settings.Save(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { ShowPrompt("Notifications", "Notification setting could not be saved."); }
            lock (_notificationQueue) _notificationQueue.Clear();
            _preview = null;
            KillTimer(_hwnd, (UIntPtr)PreviewTimer);
            UpdateTarget();
            _dirty = true;
        }
        else { _notificationPrompt = true; _notifications.Start(true); }
    }

    private void ToggleWeather()
    {
        if (_settings.WeatherEnabled)
        {
            _settings.WeatherEnabled = false;
            _weather?.Dispose();
            _weather = null;
            try { _settings.Save(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { ShowPrompt("Weather", "Weather stopped, but the setting could not be saved and may return after restart."); }
            InvalidatePanel();
            return;
        }
        ShowPrompt("Enable weather?", "Uses your IP address at ipwho.is to estimate location. Approximate coordinates are sent to Open-Meteo.", "Enable", () =>
        {
            _settings.WeatherEnabled = true;
            try { _settings.Save(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _settings.WeatherEnabled = false;
                ShowPrompt("Weather", "Could not save the weather setting.");
                return;
            }
            StartWeather();
            InvalidatePanel();
        });
    }

    private void StartWeather()
    {
        if (_taskTest) _testWeatherToggled = true;
        _weather?.Dispose();
        _weather = new WeatherService(() => PostMessage(_hwnd, WM_APP_UPDATE, IntPtr.Zero, IntPtr.Zero));
        _weather.Start();
    }

    private void ToggleConnectivity()
    {
        bool enabled = !_settings.ConnectivityAlertsEnabled;
        if (_taskTest && enabled) _testConnectivityToggled = true;
        _settings.ConnectivityAlertsEnabled = enabled;
        try { _settings.Save(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _settings.ConnectivityAlertsEnabled = !enabled;
            ShowPrompt("Connectivity", "Could not save the alert setting.");
            return;
        }
        if (enabled) StartConnectivity();
        else { _connectivity?.Dispose(); _connectivity = null; }
        InvalidatePanel();
    }

    private void StartConnectivity()
    {
        _connectivity?.Dispose();
        ConnectivityService? service = null;
        service = new ConnectivityService(preview =>
        {
            if (ReferenceEquals(_connectivity, service) && _settings.ConnectivityAlertsEnabled) EnqueueNotification(preview);
        });
        _connectivity = service;
        service.Start();
    }

    private void ToggleVolumeHud()
    {
        bool enabled = !_settings.VolumeHudEnabled;
        if (_taskTest && enabled) _testVolumeToggled = true;
        _settings.VolumeHudEnabled = enabled;
        try { _settings.Save(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _settings.VolumeHudEnabled = !enabled;
            ShowPrompt("Volume HUD", "Could not save the HUD setting.");
            return;
        }
        if (enabled) StartVolumeHud();
        else
        {
            _volumeHud?.Dispose();
            _volumeHud = null;
            if (_preview?.HudSource == "volume") ShowNextNotification();
        }
        InvalidatePanel();
    }

    private void StartVolumeHud()
    {
        _volumeHud?.Dispose();
        _volumeHud = new VolumeHudService(PublishHud);
        _volumeHud.Start();
    }

    private void ToggleBrightnessHud()
    {
        bool enabled = !_settings.BrightnessHudEnabled;
        if (_taskTest && enabled) _testBrightnessToggled = true;
        _settings.BrightnessHudEnabled = enabled;
        try { _settings.Save(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _settings.BrightnessHudEnabled = !enabled;
            ShowPrompt("Brightness HUD", "Could not save the HUD setting.");
            return;
        }
        if (enabled) StartBrightnessHud();
        else
        {
            _brightnessHud?.Dispose();
            _brightnessHud = null;
            if (_preview?.HudSource == "brightness") ShowNextNotification();
        }
        InvalidatePanel();
    }

    private void StartBrightnessHud()
    {
        _brightnessHud?.Dispose();
        _brightnessHud = new BrightnessHudService(PublishHud);
        _brightnessHud.Start();
    }

    private void PublishHud(NotificationPreview preview)
    {
        Interlocked.Exchange(ref _pendingHud, preview);
        PostMessage(_hwnd, HudMessage, IntPtr.Zero, IntPtr.Zero);
    }

    private void ToggleLyrics()
    {
        bool enabled = !_settings.LyricsEnabled;
        if (_taskTest && !enabled) _testLyricsToggled = true;
        _settings.LyricsEnabled = enabled;
        try { _settings.Save(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _settings.LyricsEnabled = !enabled;
            ShowPrompt("Lyrics", "Could not save the lyrics setting.");
            return;
        }
        if (enabled) StartLyrics();
        else { _lyrics?.Dispose(); _lyrics = null; }
        UpdateTarget();
        InvalidatePanel();
        _dirty = true;
    }

    private void StartLyrics()
    {
        _lyrics?.Dispose();
        _lyrics = new LyricsService(() => PostMessage(_hwnd, WM_APP_UPDATE, IntPtr.Zero, IntPtr.Zero));
        _lyrics.Update(_media?.Snapshot);
    }
}
