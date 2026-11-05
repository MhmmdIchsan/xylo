// Layered-window rendering approach adapted from WinIsland; see third-party/WinIsland.LICENSE.
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;
using static Xylo.Native;

namespace Xylo;

internal sealed partial class IslandApp : IDisposable
{
    public const float CanvasWidth = 440;
    private const float CanvasHeight = 420;
    private const uint CollapseTimer = 1, SmokeTimer = 2, BatteryTimer = 3, ChargeAnimationTimer = 4, BlinkTimer = 5, BlinkFrameTimer = 6, FocusTickTimer = 7, PreviewTimer = 8, NoticeTimer = 9;
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
    private float _scale = 1;
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
    private NotificationPreview? _pendingHud;
    private readonly Queue<NotificationPreview> _notificationQueue = new();
    private NotificationPreview? _preview;
    private bool _notificationPrompt;
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
        _settings = smokeTest || taskTest ? new Settings { Transient = true } : Settings.Load();
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
            _agents = new AgentBridge(() => PostMessage(_hwnd, WM_APP_UPDATE, IntPtr.Zero, IntPtr.Zero));
            _agents.Start();
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
        if (!expanded && (_composer != null || _review != null || _prompt != null || _panel != null)) return;
        if (_expanded == expanded) return;
        _expanded = expanded;
        UpdateTarget();
    }

    private void UpdateTarget()
    {
        bool needsDetail = _media?.Snapshot != null || _agents?.Pending != null || _agents?.Notice != null || _preview != null;
        double targetWidth = _prompt != null || _review != null || _composer != null || _panel != null || _expanded ? 400 : (needsDetail ? 400 : _agents?.Activity != null ? 260 : 180);
        double targetHeight = _prompt != null ? 300 : _review != null ? 400 : _composer != null ? 300 : _panel != null ? 380 : _expanded ? 236 : (needsDetail ? 68 : 42);
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
                    else if (tx is >= 24 and <= 376 && ty is >= 132 and <= 166) { _composer.SelectField(true); FocusComposer(); }
                    else if (ty is >= 240 and <= 282 && tx is >= 202 and <= 278) CloseComposer();
                    else if (ty is >= 240 and <= 282 && tx is >= 286 and <= 376) SaveComposer();
                    _dirty = true;
                    return IntPtr.Zero;
                }
                if (_review != null)
                {
                    float rx = (short)(lParam.ToInt64() & 0xffff) / _scale - (CanvasWidth - (float)_width.Value) / 2;
                    float ry = (short)((lParam.ToInt64() >> 16) & 0xffff) / _scale;
                    if (_height.Value >= 390)
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
                if (_agents?.Pending is { } request)
                {
                    _review = new ApprovalReview(request, _renderer.WrapReview);
                    _expanded = true;
                    UpdateTarget();
                    _dirty = true;
                    return IntPtr.Zero;
                }
                if (_panel != null)
                {
                    float px = (short)(lParam.ToInt64() & 0xffff) / _scale - (CanvasWidth - (float)_width.Value) / 2;
                    float py = (short)((lParam.ToInt64() >> 16) & 0xffff) / _scale;
                    HandlePanelClick(px, py);
                    return IntPtr.Zero;
                }
                if (_agents?.Notice != null) return IntPtr.Zero;
                if (_expanded)
                {
                    float px = (short)(lParam.ToInt64() & 0xffff) / _scale - (CanvasWidth - (float)_width.Value) / 2;
                    float py = (short)((lParam.ToInt64() >> 16) & 0xffff) / _scale;
                    if (px >= _width.Value - 104 && px <= _width.Value - 68 && py is >= 4 and <= 40)
                    { ShowMenu(); return IntPtr.Zero; }
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
                        _slide = Math.Clamp((int)((localX - 24) / 88), 0, 3);
                        if (_taskTest) _testSlides |= 1 << _slide;
                        _dirty = true;
                        return IntPtr.Zero;
                    }
                    if (_slide == 1 && !_settings.WeatherEnabled && my is >= 175 and <= 220 && localX is >= 24 and <= 180)
                    { ToggleWeather(); return IntPtr.Zero; }
                    if (_slide == 2 && my is >= 175 and <= 220 && localX is >= 24 and <= 180)
                    { ShowMenu(); NavigatePanel(PanelPage.Tasks); return IntPtr.Zero; }
                    if (_slide == 3 && _media?.Snapshot != null && my is >= 180 and <= 225)
                    {
                        if (mx >= left + 120 && mx < left + 170) _media.Control("previous");
                        else if (mx >= left + 170 && mx < left + 220) _media.Control("toggle");
                        else if (mx >= left + 220 && mx < left + 270) _media.Control("next");
                    }
                }
                return IntPtr.Zero;
            case WM_APP_UPDATE:
                _lyrics?.Update(_media?.Snapshot);
                if (_agents?.Pending != null && _panel != null) { _panel = null; _panelView = null; _menuOpen = false; }
                if (_review != null && !ReferenceEquals(_agents?.Pending, _review.Request)) _review = null;
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
                return IntPtr.Zero;
            case 0x031D: // WM_CLIPBOARDUPDATE
                if (_clipboardListening) _clipboard.Capture(hwnd);
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
                    if (_focus.Tick()) KillTimer(hwnd, (UIntPtr)FocusTickTimer);
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

    private void BuildSurface()
    {
        ReleaseSurface();
        uint dpi = GetDpiForWindow(_hwnd);
        _scale = dpi == 0 ? 1 : dpi / 96f;
        _pixelWidth = (int)Math.Ceiling(CanvasWidth * _scale);
        _pixelHeight = (int)Math.Ceiling(CanvasHeight * _scale);
        _x = (GetSystemMetrics(SM_CXSCREEN) - _pixelWidth) / 2;
        _y = _settings.Detached ? (int)(12 * _scale) : 0;
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

    private void Draw()
    {
        if (_surface is null) return;
        _renderer.Draw(_surface.Canvas, _scale, (float)_width.Value, (float)_height.Value, _settings.Detached, _battery, _clock.Elapsed.TotalSeconds, _blinkStarted, _media?.Snapshot, _lyrics?.Snapshot, _settings.LyricsEnabled, _focus, _agents?.Pending, _agents?.PendingCount ?? 0, _agents?.Activity, _preview, _agents?.Notice, _settings.Tasks, _slide, _composer, _review, _prompt, BuildPanelView(), _weather?.Snapshot, _settings.WeatherEnabled);
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
            TaskTestSucceeded = _settings.Detached && _testSlides == 15 && _testPanelPagination && _testWeatherToggled && !_settings.WeatherEnabled && _testConnectivityToggled && !_settings.ConnectivityAlertsEnabled && _testVolumeToggled && !_settings.VolumeHudEnabled && _testBrightnessToggled && !_settings.BrightnessHudEnabled && _testLyricsToggled && _settings.LyricsEnabled && task is { Title: "synthetic task", HasTime: true, Due.Year: 2030, Due.Month: 12, Due.Day: 24, Due.Hour: 13, Due.Minute: 30 };
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
        else if (key == 0x09) _composer.SelectField(!_composer.EditingDue);
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
            if (_notificationQueue.Count >= 10) _notificationQueue.Dequeue();
            _notificationQueue.Enqueue(preview);
        }
        PostMessage(_hwnd, PreviewMessage, IntPtr.Zero, IntPtr.Zero);
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
        if (_clipboardListening) RemoveClipboardFormatListener(_hwnd);
        if (_trayAdded) Shell_NotifyIcon(NIM_DELETE, ref _tray);
        if (IsWindow(_hwnd)) DestroyWindow(_hwnd);
        ReleaseSurface();
        _renderer.Dispose();
    }
}
