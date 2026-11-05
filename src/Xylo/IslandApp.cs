// Layered-window rendering approach adapted from WinIsland; see third-party/WinIsland.LICENSE.
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;
using static Xylo.Native;

namespace Xylo;

internal sealed partial class IslandApp : IDisposable
{
    public const float CanvasWidth = 700;
    private const float CanvasHeight = 420;
    private const uint CollapseTimer = 1, SmokeTimer = 2, BatteryTimer = 3, ChargeAnimationTimer = 4, BlinkTimer = 5, BlinkFrameTimer = 6, FocusTickTimer = 7, PreviewTimer = 8, NoticeTimer = 9, ClockTimer = 10, AgentFrameTimer = 11;
    private const uint PreviewMessage = 0x8004, NotificationStatusMessage = 0x8005, HudMessage = 0x8006;
    private readonly WndProc _windowProcedure;
    private readonly IslandRenderer _renderer = new();
    private readonly Spring _width = new(initial: 180);
    private readonly Spring _height = new(initial: 42);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly bool _smokeTest;
    private readonly bool _taskTest;
    public bool TaskTestSucceeded { get; private set; }
    private readonly Settings _settings;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private IntPtr _hwnd, _memoryDc, _bitmap, _oldBitmap;
    private SKSurface? _surface;
    private NOTIFYICONDATA _tray;
    private bool _trayAdded, _expanded, _dirty = true, _quit, _hotkeyRegistered;
    private bool _animating, _trackingMouse, _menuOpen;
    private int _slide;
    private int _testSlides;
    private bool _testClipboardToggled;
    private int _testFocusActions;
    private float _scale = 1;
    private RECT _displayBounds;
    private int _pixelWidth, _pixelHeight, _x, _y, _smokeTicks;
    private double _lastFrame;
    private long _renderCount;
    private PerformanceSample? _before, _beforeIdle;
    private long _idleRenderStart;
    private BatteryStatus _battery;
    private bool _batteryInitialized;
    private MediaService? _media;
    private LyricsService? _lyrics;
    private AgentBridge? _agents;
    private NotificationService? _notifications;
    private WeatherService? _weather;
    private ConnectivityService? _connectivity;
    private VolumeHudService? _volumeHud;
    private BrightnessHudService? _brightnessHud;
    private CodexLimitService? _codexLimits;
    private NotificationPreview? _pendingHud;
    private readonly Queue<NotificationPreview> _notificationQueue = new();
    private NotificationPreview? _preview;
    private bool _notificationPrompt;
    private volatile bool _doNotDisturb;
    private bool _testDndToggled;
    private readonly ClipboardHistory _clipboard = new();
    private readonly FocusTimer _focus = new();
    private bool _clipboardListening;
    private double _blinkStarted = -1;
    private TaskComposer? _composer;
    private ApprovalReview? _review;
    private InlinePrompt? _prompt;
    private IntPtr _previousForeground;
    private IntPtr _originalWindowStyle;

    public IslandApp(bool smokeTest, bool taskTest = false)
    {
        _smokeTest = smokeTest;
        _taskTest = taskTest;
        _settings = smokeTest || taskTest ? new Settings { Transient = true, ConnectivityAlertsEnabled = false, VolumeHudEnabled = false } : Settings.Load();
        _doNotDisturb = _settings.DoNotDisturb;
        _slide = Math.Clamp(_settings.DashboardSlide, 0, 6);
        _windowProcedure = WindowProcedure;
    }

    public void Run()
    {
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        var windowClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = _windowProcedure,
            hInstance = GetModuleHandle(null),
            hCursor = LoadCursor(IntPtr.Zero, IDC_ARROW),
            lpszClassName = "Xylo.Desktop.Window"
        };
        if (RegisterClassEx(ref windowClass) == 0) throw new Win32Exception();
        _hwnd = CreateWindowEx(WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            windowClass.lpszClassName, "Xylo", WS_POPUP, 0, 0, 1, 1,
            IntPtr.Zero, IntPtr.Zero, windowClass.hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new Win32Exception();
        if (!_smokeTest && !_taskTest && _settings.ClipboardEnabled) _clipboardListening = AddClipboardFormatListener(_hwnd);
        if (!_smokeTest && !_taskTest)
        {
            _notifications = new NotificationService(EnqueueNotification,
                () => PostMessage(_hwnd, NotificationStatusMessage, IntPtr.Zero, IntPtr.Zero));
            if (_settings.NotificationsEnabled) _notifications.Start(false);
            _agents = new AgentBridge(() => PostMessage(_hwnd, WM_APP_UPDATE, IntPtr.Zero, IntPtr.Zero),
                activity => EnqueueNotification(new NotificationPreview("AI Agent", activity.Agent + " finished", activity.Project)));
            _agents.Start();
            _codexLimits = new CodexLimitService(() => PostMessage(_hwnd, WM_APP_UPDATE, IntPtr.Zero, IntPtr.Zero));
            if (_settings.LyricsEnabled) StartLyrics();
            _media = new MediaService(() => PostMessage(_hwnd, WM_APP_UPDATE, IntPtr.Zero, IntPtr.Zero));
            _media.Start();
            if (_settings.WeatherEnabled) StartWeather();
            if (_settings.ConnectivityAlertsEnabled) StartConnectivity();
            if (_settings.VolumeHudEnabled) StartVolumeHud();
            if (_settings.BrightnessHudEnabled) StartBrightnessHud();
        }
        RefreshBattery();
        if (!_smokeTest) SetTimer(_hwnd, (UIntPtr)BatteryTimer, 60000, IntPtr.Zero);
        if (!_smokeTest) SetTimer(_hwnd, (UIntPtr)BlinkTimer, 8000, IntPtr.Zero);
        if (!_smokeTest) SetTimer(_hwnd, (UIntPtr)ClockTimer, 60000, IntPtr.Zero);
        BuildSurface();
        Draw();
        ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
        AddTrayIcon();
        // Ctrl + Alt + X, with MOD_NOREPEAT. A collision does not prevent startup.
        _hotkeyRegistered = RegisterHotKey(_hwnd, 1, 0x4003, 0x58);
        if (_taskTest) { _expanded = true; UpdateTarget(); }
        if (_smokeTest && SetTimer(_hwnd, (UIntPtr)SmokeTimer, 500, IntPtr.Zero) == UIntPtr.Zero)
            throw new Win32Exception();

        while (!_quit)
        {
            while (PeekMessage(out var message, IntPtr.Zero, 0, 0, PM_REMOVE))
            {
                if (message.message == WM_QUIT) { _quit = true; break; }
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
            if (_quit) break;
            if (_animating)
            {
                double now = _clock.Elapsed.TotalSeconds;
                double dt = Math.Clamp(now - _lastFrame, 0.0001, 0.1);
                _lastFrame = now;
                _animating = _width.Step(dt) | _height.Step(dt);
                _dirty = true;
            }
            if (_dirty) Draw();
            if (_animating)
            {
                // Pace active animation with desktop composition instead of coarse WM_TIMER ticks.
                double beforePresent = _clock.Elapsed.TotalSeconds;
                int result = DwmFlush();
                if (result != 0 || _clock.Elapsed.TotalSeconds - beforePresent < 0.001)
                    Thread.Sleep(8);
                continue;
            }
            // No polling or frame loop at idle. Windows wakes us for actual messages.
            MsgWaitForMultipleObjects(0, IntPtr.Zero, false, INFINITE, QS_ALLINPUT);
        }
    }

    private void Toggle() => SetExpanded(!_expanded);

    private void SetExpanded(bool expanded)
    {
        KillTimer(_hwnd, (UIntPtr)CollapseTimer);
        if (!expanded && (_composer != null || _prompt != null || _panel != null)) return;
        if (expanded) BeginHoverReview();
        if (_expanded == expanded) { if (expanded && _review != null) UpdateTarget(); return; }
        _expanded = expanded;
        if (!_smokeTest) SetTimer(_hwnd, (UIntPtr)ClockTimer, expanded ? 1000u : 60000u, IntPtr.Zero);
        UpdateTarget();
    }

    private void UpdateTarget()
    {
        bool needsDetail = (_slide == 4 && _media?.Snapshot != null) || _preview != null;
        bool needsApproval = _agents?.Pending != null || _agents?.Notice != null;
        bool wideMusic = _expanded && _slide == 4 && _settings.LyricsEnabled && _prompt == null && _review == null && _composer == null && _panel == null && _agents?.Pending == null && _agents?.Notice == null && _preview == null;
        double targetWidth = wideMusic ? Math.Min(660, Math.Max(400, (_displayBounds.right - _displayBounds.left) / _scale - 20)) : _prompt != null || _composer != null || _panel != null || _expanded ? 400 : (needsApproval ? 300 : needsDetail ? 400 : _slide == 1 && _agents?.Activity != null ? 300 : 220);
        double targetHeight = _prompt != null ? 300 : _review != null && _expanded ? 400 : _composer != null ? 300 : _panel != null ? 380 : _expanded ? (_slide == 1 && !needsApproval && _preview == null ? 300 : 236) : needsApproval ? 42 : (needsDetail ? 68 : 42);
        if (_width.Target == targetWidth && _height.Target == targetHeight) return;
        _width.Target = targetWidth;
        _height.Target = targetHeight;
        if (!_animating) _lastFrame = _clock.Elapsed.TotalSeconds;
        _animating = true;
    }

    private IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == _taskbarCreated && _taskbarCreated != 0)
        {
            _trayAdded = false;
            AddTrayIcon();
            return IntPtr.Zero;
        }
        switch (message)
        {
            case 0x8019: // Query target width for the isolated interaction test.
                if (_taskTest) return new IntPtr((int)_width.Target);
                break;
            case 0x801A:
                if (_taskTest) return new IntPtr((int)Math.Round(_width.Value));
                break;
            case 0x801B:
                if (_taskTest) return new IntPtr((int)Math.Round(_height.Value) - (int)Math.Round(_height.Target));
                break;
            case 0x801C:
                if (_taskTest) return new IntPtr((int)Math.Round(_scale * 1000));
                break;
            case WM_NCHITTEST:
                float x = ((short)(lParam.ToInt64() & 0xffff) - _x) / _scale;
                float y = ((short)((lParam.ToInt64() >> 16) & 0xffff) - _y) / _scale;
                return new IntPtr(IsInsidePill(x, y) ? HTCLIENT : HTTRANSPARENT);
            case WM_MOUSEMOVE:
                if (_smokeTest) return IntPtr.Zero;
                if (!_trackingMouse)
                {
                    var tracking = new TRACKMOUSEEVENT
                    {
                        cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(),
                        dwFlags = TME_LEAVE, hwndTrack = hwnd
                    };
                    _trackingMouse = TrackMouseEvent(ref tracking);
                }
                SetExpanded(true);
                return IntPtr.Zero;
            case WM_MOUSELEAVE:
                if (_smokeTest) return IntPtr.Zero;
                _trackingMouse = false;
                // Brief grace period prevents flicker while crossing an animated edge.
                if (!_menuOpen) SetTimer(hwnd, (UIntPtr)CollapseTimer, 180, IntPtr.Zero);
                return IntPtr.Zero;
            case WM_LBUTTONUP:
                if (_prompt != null)
                {
                    float px = (short)(lParam.ToInt64() & 0xffff) / _scale - (CanvasWidth - (float)_width.Value) / 2;
                    float py = (short)((lParam.ToInt64() >> 16) & 0xffff) / _scale;
                    if (py is >= 240 and <= 282 && px is >= 202 and <= 278) ClosePrompt(false);
                    else if (py is >= 240 and <= 282 && px is >= 286 and <= 376) ClosePrompt(true);
                    return IntPtr.Zero;
                }
                if (_composer != null)
                {
                    float tx = (short)(lParam.ToInt64() & 0xffff) / _scale - (CanvasWidth - (float)_width.Value) / 2;
                    float ty = (short)((lParam.ToInt64() >> 16) & 0xffff) / _scale;
                    if (tx is >= 24 and <= 376 && ty is >= 75 and <= 109) { _composer.SelectField(false); FocusComposer(); }
                    else if (ty is >= 132 and <= 166)
                    {
                        if (tx is >= 24 and <= 56) _composer.ShiftDate(-1);
                        else if (tx is >= 232 and <= 264) _composer.ShiftDate(1);
                        else if (tx is >= 272 and <= 376) _composer.ClearDate();
                    }
                    else if (ty is >= 170 and <= 204)
                    {
                        if (tx is >= 24 and <= 56) _composer.ShiftTime(-60);
                        else if (tx is >= 120 and <= 152) _composer.ShiftTime(60);
                        else if (tx is >= 176 and <= 208) _composer.ShiftTime(-15);
                        else if (tx is >= 264 and <= 296) _composer.ShiftTime(15);
                        else if (tx is >= 304 and <= 376) _composer.ClearTime();
                    }
                    else if (ty is >= 240 and <= 282 && tx is >= 202 and <= 278) CloseComposer();
                    else if (ty is >= 240 and <= 282 && tx is >= 286 and <= 376) SaveComposer();
                    _dirty = true;
                    return IntPtr.Zero;
                }
                if (_review != null)
                {
                    float rx = (short)(lParam.ToInt64() & 0xffff) / _scale - (CanvasWidth - (float)_width.Value) / 2;
                    float ry = (short)((lParam.ToInt64() >> 16) & 0xffff) / _scale;
                    if (_expanded && _height.Value >= 390)
                    {
                        if (ry is >= 304 and <= 338 && rx is >= 24 and <= 98) _review.Previous();
                        else if (ry is >= 304 and <= 338 && rx is >= 108 and <= 182) _review.Next();
                        else if (ry is >= 350 and <= 396 && rx is >= 24 and <= 124) RespondToApproval("defer");
                        else if (ry is >= 350 and <= 396 && rx is >= 140 and <= 240) RespondToApproval("deny");
                        else if (ry is >= 350 and <= 396 && rx is >= 256 and <= 376 && _review.CanApprove) RespondToApproval("allow");
                    }
                    _dirty = true;
                    return IntPtr.Zero;
                }
                if (_agents?.Pending != null) return IntPtr.Zero;
                if (_panel != null)
                {
                    float px = (short)(lParam.ToInt64() & 0xffff) / _scale - (CanvasWidth - (float)_width.Value) / 2;
                    float py = (short)((lParam.ToInt64() >> 16) & 0xffff) / _scale;
                    HandlePanelClick(px, py);
                    return IntPtr.Zero;
                }
                if (_agents?.Notice is { } notice)
                {
                    if (notice.InputRequired) RestoreAgentWindow(notice);
                    return IntPtr.Zero;
                }
                if (_expanded)
                {
                    float px = (short)(lParam.ToInt64() & 0xffff) / _scale - (CanvasWidth - (float)_width.Value) / 2;
                    float py = (short)((lParam.ToInt64() >> 16) & 0xffff) / _scale;
                    if (px >= _width.Value - 104 && px <= _width.Value - 68 && py is >= 4 and <= 40)
                    { ShowMenu(); return IntPtr.Zero; }
                    if (px >= _width.Value - 72 && px <= _width.Value - 40 && py is >= 4 and <= 40)
                    { ToggleDnd(); return IntPtr.Zero; }
                }
                if (_preview != null) return IntPtr.Zero;
                if (_expanded)
                {
                    float mx = (short)(lParam.ToInt64() & 0xffff) / _scale;
                    float my = (short)((lParam.ToInt64() >> 16) & 0xffff) / _scale;
                    float left = (CanvasWidth - (float)_width.Value) / 2;
                    float localX = mx - left;
                    if (my is >= 48 and <= 78 && localX is >= 24 and < 376)
                    {
                        var visible = _settings.VisibleTabs();
                        _slide = visible[Math.Clamp((int)((localX - 24) / (352f / visible.Length)), 0, visible.Length - 1)];
                        _settings.DashboardSlide = _slide;
                        try { _settings.Save(); }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                        { ShowPrompt("View", "Could not save the selected view."); }
                        if (_taskTest) _testSlides |= 1 << _slide;
                        SyncAgentAnimation();
                        UpdateTarget();
                        _dirty = true;
                        return IntPtr.Zero;
                    }
                    if (_slide == 2 && !_settings.WeatherEnabled && my is >= 175 and <= 220 && localX is >= 24 and <= 180)
                    { ToggleWeather(); return IntPtr.Zero; }
                    if (_slide == 3 && my is >= 175 and <= 220 && localX is >= 24 and <= 180)
                    { ShowMenu(); NavigatePanel(PanelPage.Tasks); return IntPtr.Zero; }
                    if (_slide == 4 && my is >= 180 and <= 220 && localX is >= 290 and <= 376)
                    { ToggleLyrics(); return IntPtr.Zero; }
                    if (_slide == 4 && _media?.Snapshot is { CanSeek: true, Duration: > 0 } && my is >= 140 and <= 168 && localX is >= 130 and <= 376)
                    { _media.Seek((localX - 130) / 246); return IntPtr.Zero; }
                    if (_slide == 5)
                    {
                        if (my is >= 175 and <= 220 && localX is >= 24 and <= 180) { ToggleClipboard(); if (_taskTest && _clipboardListening) _testClipboardToggled = true; _dirty = true; return IntPtr.Zero; }
                        if (_clipboardListening && my is >= 175 and <= 220 && localX is >= 200 and <= 376) { _clipboard.Clear(); _dirty = true; return IntPtr.Zero; }
                        if (_clipboardListening && localX is >= 24 and <= 376 && my is >= 108 and < 168)
                        {
                            int item = (int)((my - 108) / 20);
                            if (item < _clipboard.Items.Count && !_clipboard.Copy(_hwnd, item)) ShowPrompt("Clipboard", "Could not copy the selected item.");
                            return IntPtr.Zero;
                        }
                    }
                    if (_slide == 6)
                    {
                        if (my is >= 125 and <= 165)
                        {
                            if (localX is >= 24 and <= 128) { StartFocus("Focus", 25 * 60); if (_taskTest && _focus.Running) _testFocusActions |= 1; }
                            else if (localX is >= 142 and <= 246) StartFocus("Break", 5 * 60);
                            else if (localX is >= 260 and <= 376) StartFocus("Long break", 15 * 60);
                        }
                        else if (my is >= 175 and <= 215)
                        {
                            if (localX is >= 24 and <= 180) { _focus.Toggle(); if (_focus.Running) SetTimer(_hwnd, (UIntPtr)FocusTickTimer, 1000, IntPtr.Zero); else KillTimer(_hwnd, (UIntPtr)FocusTickTimer); if (_taskTest && _focus.Active && !_focus.Running) _testFocusActions |= 2; }
                            else if (localX is >= 200 and <= 376) { _focus.Clear(); KillTimer(_hwnd, (UIntPtr)FocusTickTimer); if (_taskTest && !_focus.Active) _testFocusActions |= 4; }
                        }
                        _dirty = true;
                        return IntPtr.Zero;
                    }
                    if (_slide == 4 && _media?.Snapshot != null && my is >= 180 and <= 225)
                    {
                        if (mx >= left + 120 && mx < left + 170) _media.Control("previous");
                        else if (mx >= left + 170 && mx < left + 220) _media.Control("toggle");
                        else if (mx >= left + 220 && mx < left + 270) _media.Control("next");
                    }
                }
                return IntPtr.Zero;
            case WM_APP_UPDATE:
                _lyrics?.Update(_media?.Snapshot);
                SyncAgentAnimation();
                if (_agents?.Pending != null && _panel != null) { _panel = null; _panelView = null; _menuOpen = false; }
                if (_review != null && !ReferenceEquals(_agents?.Pending, _review.Request)) _review = null;
                if (_expanded) BeginHoverReview();
                if (_agents?.Notice != null) SetTimer(hwnd, (UIntPtr)NoticeTimer, 5000, IntPtr.Zero);
                else KillTimer(hwnd, (UIntPtr)NoticeTimer);
                UpdateTarget();
                _dirty = true;
                return IntPtr.Zero;
            case PreviewMessage:
                if (_preview == null) ShowNextNotification();
                return IntPtr.Zero;
            case HudMessage:
                var hud = Interlocked.Exchange(ref _pendingHud, null);
                if (hud != null && (hud.HudSource == "volume" && _settings.VolumeHudEnabled || hud.HudSource == "brightness" && _settings.BrightnessHudEnabled))
                {
                    if (_preview is { IsHud: false } previous)
                    {
                        lock (_notificationQueue)
                        {
                            if (_notificationQueue.Count >= 10) _notificationQueue.Dequeue();
                            _notificationQueue.Enqueue(previous);
                        }
                    }
                    _preview = hud;
                    SetTimer(hwnd, (UIntPtr)PreviewTimer, 1800, IntPtr.Zero);
                    UpdateTarget();
                    _dirty = true;
                }
                return IntPtr.Zero;
            case NotificationStatusMessage:
                if (_notifications != null && !_notifications.Requesting)
                {
                    if (_notificationPrompt && !_notifications.Active && _notifications.LastError != null)
                        ShowPrompt("Notifications", _notifications.LastError);
                    if (_notificationPrompt && _notifications.Active)
                    {
                        _settings.NotificationsEnabled = true;
                        try { _settings.Save(); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                    }
                    _notificationPrompt = false;
                    if (_panel == PanelPage.Home) InvalidatePanel();
                }
                _dirty = true;
                return IntPtr.Zero;
            case 0x031D: // WM_CLIPBOARDUPDATE
                if (_clipboardListening) _clipboard.Capture(hwnd);
                if (_slide == 5) _dirty = true;
                if (_panel == PanelPage.Clipboard) InvalidatePanel();
                return IntPtr.Zero;
            case 0x0312: // WM_HOTKEY
                Toggle();
                return IntPtr.Zero;
            case WM_RBUTTONUP:
                if (_composer != null || _review != null || _prompt != null) return IntPtr.Zero;
                ShowMenu();
                return IntPtr.Zero;
            case WM_MOUSEWHEEL:
                if (_prompt != null || _composer != null) return IntPtr.Zero;
                if (_review != null)
                {
                    int delta = (short)((wParam.ToInt64() >> 16) & 0xffff);
                    if (delta < 0) _review.Next(); else if (delta > 0) _review.Previous();
                    _dirty = true;
                    return IntPtr.Zero;
                }
                if (_panel != null)
                {
                    int delta = (short)((wParam.ToInt64() >> 16) & 0xffff);
                    if (delta < 0) _panelPage++; else if (delta > 0) _panelPage = Math.Max(0, _panelPage - 1);
                    InvalidatePanel();
                    return IntPtr.Zero;
                }
                break;
            case WM_TRAYICON:
                if ((uint)lParam.ToInt64() == WM_RBUTTONUP) ShowMenu();
                else if ((uint)lParam.ToInt64() == WM_LBUTTONUP) { if (_panel != null) ClosePanel(); else Toggle(); }
                return IntPtr.Zero;
            case 0x0113: // WM_TIMER
                if (wParam.ToInt64() == CollapseTimer)
                {
                    KillTimer(hwnd, (UIntPtr)CollapseTimer);
                    GetCursorPos(out var cursor);
                    if (!_menuOpen && !IsInsidePill((cursor.X - _x) / _scale, (cursor.Y - _y) / _scale))
                        SetExpanded(false);
                }
                else if (_smokeTest && wParam.ToInt64() == SmokeTimer) SmokeTick();
                else if (wParam.ToInt64() == BatteryTimer) RefreshBattery();
                else if (wParam.ToInt64() == ChargeAnimationTimer) _dirty = true;
                else if (wParam.ToInt64() == AgentFrameTimer)
                {
                    if (_agents?.Activity?.Working != true) KillTimer(hwnd, (UIntPtr)AgentFrameTimer);
                    else _dirty = true;
                }
                else if (wParam.ToInt64() == ClockTimer) _dirty = true;
                else if (wParam.ToInt64() == BlinkTimer)
                {
                    _blinkStarted = _clock.Elapsed.TotalSeconds;
                    SetTimer(hwnd, (UIntPtr)BlinkFrameTimer, 30, IntPtr.Zero);
                }
                else if (wParam.ToInt64() == BlinkFrameTimer)
                {
                    if (_clock.Elapsed.TotalSeconds - _blinkStarted >= 0.3)
                    {
                        _blinkStarted = -1;
                        KillTimer(hwnd, (UIntPtr)BlinkFrameTimer);
                    }
                    _dirty = true;
                }
                else if (wParam.ToInt64() == FocusTickTimer)
                {
                    if (_focus.Tick()) { KillTimer(hwnd, (UIntPtr)FocusTickTimer); EnqueueNotification(new NotificationPreview("Focus", _focus.Label + " complete", "Your timer has finished.")); }
                    if (_panel == PanelPage.Focus) InvalidatePanel();
                    _dirty = true;
                }
                else if (wParam.ToInt64() == PreviewTimer)
                {
                    if (_agents?.Pending == null && _agents?.Notice == null) ShowNextNotification();
                }
                else if (wParam.ToInt64() == NoticeTimer)
                {
                    if (_agents?.Notice == null) KillTimer(hwnd, (UIntPtr)NoticeTimer);
                    UpdateTarget();
                    _dirty = true;
                }
                return IntPtr.Zero;
            case 0x0218: // WM_POWERBROADCAST
                RefreshBattery();
                return new IntPtr(1);
            case 0x0008: // WM_KILLFOCUS
                if (_composer != null) { _composer.Focused = false; _dirty = true; }
                return IntPtr.Zero;
            case WM_KEYDOWN:
                if (_prompt != null) { if ((int)wParam.ToInt64() == VK_ESCAPE) ClosePrompt(false); return IntPtr.Zero; }
                if (_composer != null) { HandleComposerKey((int)wParam.ToInt64()); return IntPtr.Zero; }
                break;
            case 0x0102: // WM_CHAR
                if (_prompt != null) return IntPtr.Zero;
                if (_composer != null)
                {
                    char ch = (char)wParam.ToInt64();
                    if (!char.IsControl(ch)) _composer.Insert(ch.ToString());
                    _dirty = true;
                    return IntPtr.Zero;
                }
                break;
            case WM_DISPLAYCHANGE:
            case WM_DPICHANGED:
                BuildSurface();
                UpdateTarget();
                _dirty = true;
                return IntPtr.Zero;
            case 0x0010: // WM_CLOSE
                DestroyWindow(hwnd);
                return IntPtr.Zero;
            case WM_DESTROY:
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private bool IsInsidePill(float x, float y)
    {
        float left = (CanvasWidth - (float)_width.Value) / 2;
        float right = left + (float)_width.Value;
        float bottom = (float)_height.Value;
        if (x < left || x > right || y < 0 || y > bottom) return false;
        const float radius = 23;
        if (_settings.Detached && y < radius)
        {
            float topX = Math.Clamp(x, left + radius, right - radius);
            return Math.Pow(x - topX, 2) + Math.Pow(y - radius, 2) <= radius * radius;
        }
        if (y <= bottom - radius) return true;
        float nearestX = Math.Clamp(x, left + radius, right - radius);
        float nearestY = bottom - radius;
        return Math.Pow(x - nearestX, 2) + Math.Pow(y - nearestY, 2) <= radius * radius;
    }

    private void SyncAgentAnimation()
    {
        if (_agents?.Activity?.Working == true) SetTimer(_hwnd, (UIntPtr)AgentFrameTimer, 80, IntPtr.Zero);
        else KillTimer(_hwnd, (UIntPtr)AgentFrameTimer);
    }

    private void BeginHoverReview()
    {
        if (_review == null && _composer == null && _prompt == null && _panel == null && _agents?.Pending is { } request && !request.Completion.Task.IsCompleted)
            _review = new ApprovalReview(request, _renderer.WrapReview);
    }

    private void RestoreAgentWindow(AgentNotice notice)
    {
        var activity = _agents?.FindActivity(notice.Session);
        if (activity == null || activity.WindowHandle <= 0 || activity.WindowPid <= 0)
        { ShowPrompt("Return to agent", "The original window was not identified. Open the agent directly to answer."); return; }
        IntPtr target = new(activity.WindowHandle);
        if (!IsWindow(target) || GetWindowThreadProcessId(target, out uint pid) == 0 || pid != (uint)activity.WindowPid)
        { ShowPrompt("Return to agent", "The original window is no longer available. Open the agent directly to answer."); return; }
        if (IsIconic(target)) ShowWindow(target, 9); // SW_RESTORE
        if (!SetForegroundWindow(target))
        {
            FlashWindow(target, true);
            ShowPrompt("Return to agent", "Windows highlighted the agent window on the taskbar; select it to answer.");
        }
    }

    private void BuildSurface()
    {
        ReleaseSurface();
        var monitors = GetDisplays();
        var display = monitors[Math.Clamp(_settings.MonitorIndex, 0, monitors.Count - 1)];
        _displayBounds = display.Bounds;
        uint dpi = GetDpiForWindow(_hwnd);
        try { if (GetDpiForMonitor(display.Handle, 0, out uint monitorDpi, out _) == 0 && monitorDpi > 0) dpi = monitorDpi; } catch (DllNotFoundException) { }
        _scale = (dpi == 0 ? 1 : dpi / 96f) * (_settings.SizePreset == 0 ? 0.9f : _settings.SizePreset == 2 ? 1.1f : 1f);
        _pixelWidth = (int)Math.Ceiling(CanvasWidth * _scale);
        _pixelHeight = (int)Math.Ceiling(CanvasHeight * _scale);
        _x = _displayBounds.left + (_displayBounds.right - _displayBounds.left - _pixelWidth) / 2;
        _y = _displayBounds.top + (_settings.Detached ? (int)(12 * _scale) : 0);
        IntPtr screenDc = GetDC(IntPtr.Zero);
        try
        {
            _memoryDc = CreateCompatibleDC(screenDc);
            if (_memoryDc == IntPtr.Zero) throw new Win32Exception();
            var header = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = _pixelWidth,
                biHeight = -_pixelHeight, biPlanes = 1, biBitCount = 32
            };
            _bitmap = CreateDIBSection(screenDc, ref header, 0, out var bits, IntPtr.Zero, 0);
            if (_bitmap == IntPtr.Zero) throw new Win32Exception();
            _oldBitmap = SelectObject(_memoryDc, _bitmap);
            _surface = SKSurface.Create(new SKImageInfo(_pixelWidth, _pixelHeight, SKColorType.Bgra8888,
                SKAlphaType.Premul), bits, _pixelWidth * 4) ?? throw new InvalidOperationException("Cannot create rendering surface.");
        }
        finally { ReleaseDC(IntPtr.Zero, screenDc); }
    }

    private static List<(IntPtr Handle, RECT Bounds)> GetDisplays()
    {
        var displays = new List<(IntPtr, RECT)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr dc, ref RECT rect, IntPtr data) => { displays.Add((monitor, rect)); return true; }, IntPtr.Zero);
        if (displays.Count == 0) displays.Add((IntPtr.Zero, new RECT { right = GetSystemMetrics(SM_CXSCREEN), bottom = GetSystemMetrics(SM_CYSCREEN) }));
        return displays;
    }

    private void Draw()
    {
        if (_surface is null) return;
        if (_expanded && _slide == 1 && _panel == null && _prompt == null && _composer == null && _review == null && _preview == null && _agents?.Pending == null && _agents?.Notice == null) _codexLimits?.RefreshIfDue();
        _renderer.Draw(_surface.Canvas, _scale, (float)_width.Value, (float)_height.Value, _settings.Detached, _battery, _clock.Elapsed.TotalSeconds, _blinkStarted, _media?.Snapshot, _lyrics?.Snapshot, _settings.LyricsEnabled, _focus, _agents?.Pending, _agents?.PendingCount ?? 0, _agents?.Activity, _preview, _agents?.Notice, _settings.Tasks, _slide, _composer, _review, _prompt, BuildPanelView(), _weather?.Snapshot, _settings.WeatherEnabled, _notifications?.UnreadCount ?? 0, _notifications?.ActiveNotifications ?? Array.Empty<NotificationPreview>(), _notifications?.Active == true, _clipboardListening, _clipboard.Items, _settings.ShowBatteryPercent, _settings.VisibleTabs(), _slide == 1 ? _agents?.Activities ?? Array.Empty<AgentActivity>() : Array.Empty<AgentActivity>(), _doNotDisturb, _codexLimits?.Snapshot, _codexLimits?.Status ?? "Codex limits unavailable");
        _surface.Canvas.Flush();
        var destination = new POINT { X = _x, Y = _y };
        var source = new POINT();
        var size = new SIZE { cx = _pixelWidth, cy = _pixelHeight };
        var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
        if (!UpdateLayeredWindow(_hwnd, IntPtr.Zero, ref destination, ref size, _memoryDc,
            ref source, 0, ref blend, ULW_ALPHA)) throw new Win32Exception();
        _renderCount++;
        _dirty = false;
    }

    private void RefreshBattery()
    {
        var previous = _battery;
        _battery = BatteryStatus.Read();
        if (_batteryInitialized && _settings.BatteryAlertsEnabled && !_smokeTest && !_taskTest &&
            BatteryStatus.Alert(previous, _battery) is { } alert) EnqueueNotification(alert);
        _batteryInitialized = true;
        if (!_smokeTest && _battery.Charging) SetTimer(_hwnd, (UIntPtr)ChargeAnimationTimer, 50, IntPtr.Zero);
        else KillTimer(_hwnd, (UIntPtr)ChargeAnimationTimer);
        _dirty = true;
    }

    private void AddTrayIcon()
    {
        _tray = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = _hwnd, uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP, uCallbackMessage = WM_TRAYICON,
            hIcon = LoadIcon(IntPtr.Zero, IDI_APPLICATION), szTip = "Xylo — click to toggle",
            szInfo = "", szInfoTitle = ""
        };
        _trayAdded = Shell_NotifyIcon(NIM_ADD, ref _tray);
    }

    private void ToggleClipboard()
    {
        bool enable = !_clipboardListening;
        if (enable && !AddClipboardFormatListener(_hwnd))
        {
            ShowPrompt("Clipboard", "Could not enable clipboard history.");
            return;
        }
        if (!enable) { RemoveClipboardFormatListener(_hwnd); _clipboard.Clear(); }
        _clipboardListening = enable;
        _settings.ClipboardEnabled = enable;
        try { _settings.Save(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ShowPrompt("Clipboard", "Clipboard setting could not be saved.");
        }
    }

    private void StartFocus(string label, int seconds)
    {
        _focus.StartPreset(label, seconds);
        SetTimer(_hwnd, (UIntPtr)FocusTickTimer, 1000, IntPtr.Zero);
        _dirty = true;
    }

    private void SaveTasks()
    {
        try { _settings.Save(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { ShowPrompt("Tasks", "Tasks could not be saved; changes remain available until Xylo exits."); }
        _dirty = true;
    }

    private void BeginComposer(int? index)
    {
        if (_composer != null) return;
        _composer = new TaskComposer(index, index is int i ? _settings.Tasks[i] : null);
        _originalWindowStyle = GetWindowLongPtr(_hwnd, -20);
        _expanded = true;
        UpdateTarget();
        FocusComposer();
        _dirty = true;
    }

    private void ShowPrompt(string title, string message, string actionLabel = "OK", Action? confirm = null, bool dangerous = false)
    {
        _prompt = new InlinePrompt(title, _renderer.WrapReview(ApprovalReview.SafeDisplay(message)), actionLabel, confirm, dangerous);
        _expanded = true;
        UpdateTarget();
        _dirty = true;
    }

    private void ClosePrompt(bool confirmed)
    {
        var prompt = _prompt;
        _prompt = null;
        if (confirmed) prompt?.Confirm?.Invoke();
        if (_prompt == null && _composer == null && _review == null && _panel == null)
        {
            GetCursorPos(out var cursor);
            _expanded = IsInsidePill((cursor.X - _x) / _scale, (cursor.Y - _y) / _scale);
        }
        UpdateTarget();
        _dirty = true;
    }

    private void FocusComposer()
    {
        if (_composer == null) return;
        IntPtr foreground = GetForegroundWindow();
        if (foreground != IntPtr.Zero && foreground != _hwnd) _previousForeground = foreground;
        SetWindowLongPtr(_hwnd, -20, new IntPtr(_originalWindowStyle.ToInt64() & ~WS_EX_NOACTIVATE));
        if (SetForegroundWindow(_hwnd)) SetFocus(_hwnd);
        _composer.Focused = GetForegroundWindow() == _hwnd;
    }

    private void CloseComposer()
    {
        if (_composer == null) return;
        _composer = null;
        bool restoreFocus = GetForegroundWindow() == _hwnd;
        SetWindowLongPtr(_hwnd, -20, _originalWindowStyle);
        if (restoreFocus && _previousForeground != IntPtr.Zero && IsWindow(_previousForeground)) SetForegroundWindow(_previousForeground);
        GetCursorPos(out var cursor);
        _expanded = _prompt != null || IsInsidePill((cursor.X - _x) / _scale, (cursor.Y - _y) / _scale);
        UpdateTarget();
        _dirty = true;
    }

    private void SaveComposer()
    {
        if (_composer == null) return;
        var task = _composer.Build();
        if (task == null) { _dirty = true; return; }
        if (_composer.EditIndex is int index && index >= 0 && index < _settings.Tasks.Count) _settings.Tasks[index] = task;
        else if (_composer.EditIndex == null && _settings.Tasks.Count < 30) _settings.Tasks.Add(task);
        else { CloseComposer(); return; }
        if (_taskTest)
        {
            TaskTestSucceeded = _settings.Detached && _testSlides == 127 && _testDndToggled && !_doNotDisturb && _testClipboardToggled && !_clipboardListening && _testFocusActions == 7 && _testAppearanceToggled && _settings.ShowBatteryPercent && _testSizeChanged && _settings.SizePreset == 1 && _testTabToggled && _settings.HiddenTabs.Count == 0 && _testTabMoved && _settings.TabOrder.SequenceEqual(Enumerable.Range(0, 7)) && _testPanelPagination && _testWeatherToggled && !_settings.WeatherEnabled && _testConnectivityToggled && !_settings.ConnectivityAlertsEnabled && _testVolumeToggled && !_settings.VolumeHudEnabled && _testBrightnessToggled && !_settings.BrightnessHudEnabled && _testLyricsToggled && _settings.LyricsEnabled && task is { Title: "synthetic task", HasTime: true } && task.Due == DateTime.Today.AddDays(1).AddHours(1).AddMinutes(15);
        }
        else SaveTasks();
        CloseComposer();
        if (_taskTest) PostMessage(_hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero);
    }

    private void RespondToApproval(string decision)
    {
        var review = _review;
        if (review == null || !ReferenceEquals(_agents?.Pending, review.Request)) { _review = null; UpdateTarget(); return; }
        if (decision == "allow" && !review.CanApprove) return;
        review.Request.Completion.TrySetResult(decision);
        _review = null;
        GetCursorPos(out var cursor);
        _expanded = IsInsidePill((cursor.X - _x) / _scale, (cursor.Y - _y) / _scale);
        UpdateTarget();
    }

    private void HandleComposerKey(int key)
    {
        if (_composer == null) return;
        bool control = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
        if (control && key == 0x41) _composer.SelectAll();
        else if (control && key == VK_KEY_V) _composer.Insert(ClipboardHistory.ReadText(_hwnd) ?? "");
        else if (key == VK_ESCAPE) CloseComposer();
        else if (key == VK_RETURN) SaveComposer();
        else if (key == 0x09) _composer.SelectField(false);
        else if (key == VK_BACK) _composer.Backspace();
        else if (key == VK_DELETE) _composer.Delete();
        else if (key == VK_LEFT) _composer.Move(-1);
        else if (key == VK_RIGHT) _composer.Move(1);
        else if (key == VK_HOME) _composer.Home();
        else if (key == VK_END) _composer.End();
        _dirty = true;
    }

    private void EnqueueNotification(NotificationPreview preview)
    {
        lock (_notificationQueue)
        {
            if (!ShouldShowPreview(_doNotDisturb, preview)) return;
            if (_notificationQueue.Count >= 10) _notificationQueue.Dequeue();
            _notificationQueue.Enqueue(preview);
        }
        PostMessage(_hwnd, PreviewMessage, IntPtr.Zero, IntPtr.Zero);
    }

    internal static bool ShouldShowPreview(bool doNotDisturb, NotificationPreview preview) => !doNotDisturb || !preview.IsAppNotification;

    private void ToggleDnd()
    {
        bool enabled = !_doNotDisturb;
        _settings.DoNotDisturb = enabled;
        try { _settings.Save(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { _settings.DoNotDisturb = !enabled; ShowPrompt("Do not disturb", "Could not save the setting."); return; }
        lock (_notificationQueue)
        {
            _doNotDisturb = enabled;
            if (enabled)
            {
                var keep = _notificationQueue.Where(item => !item.IsAppNotification).ToArray();
                _notificationQueue.Clear();
                foreach (var item in keep) _notificationQueue.Enqueue(item);
            }
        }
        if (_taskTest && enabled) _testDndToggled = true;
        if (enabled && _preview?.IsAppNotification == true) ShowNextNotification();
        _dirty = true;
    }

    private void ShowNextNotification()
    {
        lock (_notificationQueue) _preview = _notificationQueue.Count > 0 ? _notificationQueue.Dequeue() : null;
        if (_preview == null) KillTimer(_hwnd, (UIntPtr)PreviewTimer);
        else SetTimer(_hwnd, (UIntPtr)PreviewTimer, 6000, IntPtr.Zero);
        UpdateTarget();
        _dirty = true;
    }

    private void SmokeTick()
    {
        _smokeTicks++;
        if (_smokeTicks == 1) _before = PerformanceSample.Capture();
        if (_smokeTicks is >= 2 and <= 11) Toggle();
        if (_smokeTicks == 14)
        {
            _beforeIdle = PerformanceSample.Capture();
            _idleRenderStart = _renderCount;
        }
        if (_smokeTicks == 18)
        {
            new SmokeReport(_before!, _beforeIdle!, PerformanceSample.Capture(),
                _renderCount, _renderCount - _idleRenderStart, _hotkeyRegistered, _trayAdded).Save();
            DestroyWindow(_hwnd);
        }
    }

    private void ReleaseSurface()
    {
        _surface?.Dispose();
        _surface = null;
        if (_memoryDc != IntPtr.Zero && _oldBitmap != IntPtr.Zero) SelectObject(_memoryDc, _oldBitmap);
        if (_bitmap != IntPtr.Zero) DeleteObject(_bitmap);
        if (_memoryDc != IntPtr.Zero) DeleteDC(_memoryDc);
        _bitmap = _memoryDc = _oldBitmap = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_hotkeyRegistered) UnregisterHotKey(_hwnd, 1);
        _media?.Dispose();
        _lyrics?.Dispose();
        _agents?.Dispose();
        _notifications?.Dispose();
        _weather?.Dispose();
        _connectivity?.Dispose();
        _volumeHud?.Dispose();
        _brightnessHud?.Dispose();
        _codexLimits?.Dispose();
        if (_clipboardListening) RemoveClipboardFormatListener(_hwnd);
        if (_trayAdded) Shell_NotifyIcon(NIM_DELETE, ref _tray);
        if (IsWindow(_hwnd)) DestroyWindow(_hwnd);
        ReleaseSurface();
        _renderer.Dispose();
    }
}
