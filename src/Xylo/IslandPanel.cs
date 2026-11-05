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
                rows.Add(new("Quit Xylo", () => DestroyWindow(_hwnd)));
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
                NavigatePanel(_panel == PanelPage.TaskDetails ? PanelPage.Tasks : PanelPage.Home);
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
        _y = _settings.Detached ? (int)(12 * _scale) : 0;
        _dirty = true;
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
}
