// Layered-window rendering approach adapted from WinIsland; see third-party/WinIsland.LICENSE.
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;
using static Xylo.Native;

namespace Xylo;

internal sealed class IslandApp : IDisposable
{
    public const float CanvasWidth = 440;
    private const float CanvasHeight = 250;
    private const uint CollapseTimer = 1, SmokeTimer = 2, BatteryTimer = 3, ChargeAnimationTimer = 4;
    private readonly WndProc _windowProcedure;
    private readonly IslandRenderer _renderer = new();
    private readonly Spring _width = new(initial: 180);
    private readonly Spring _height = new(initial: 42);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly bool _smokeTest;
    private readonly Settings _settings;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private IntPtr _hwnd, _memoryDc, _bitmap, _oldBitmap;
    private SKSurface? _surface;
    private NOTIFYICONDATA _tray;
    private bool _trayAdded, _expanded, _dirty = true, _quit, _hotkeyRegistered;
    private bool _animating, _trackingMouse, _menuOpen;
    private float _scale = 1;
    private int _pixelWidth, _pixelHeight, _x, _y, _smokeTicks;
    private double _lastFrame;
    private long _renderCount;
    private PerformanceSample? _before, _beforeIdle;
    private long _idleRenderStart;
    private BatteryStatus _battery;

    public IslandApp(bool smokeTest)
    {
        _smokeTest = smokeTest;
        _settings = smokeTest ? new Settings() : Settings.Load();
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
        RefreshBattery();
        if (!_smokeTest) SetTimer(_hwnd, (UIntPtr)BatteryTimer, 60000, IntPtr.Zero);
        BuildSurface();
        Draw();
        ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
        AddTrayIcon();
        // Ctrl + Alt + X, with MOD_NOREPEAT. A collision does not prevent startup.
        _hotkeyRegistered = RegisterHotKey(_hwnd, 1, 0x4003, 0x58);
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
        if (_expanded == expanded) return;
        _expanded = expanded;
        _width.Target = _expanded ? 400 : 180;
        _height.Target = _expanded ? 236 : 42;
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
                _trackingMouse = false;
                // Brief grace period prevents flicker while crossing an animated edge.
                if (!_menuOpen) SetTimer(hwnd, (UIntPtr)CollapseTimer, 180, IntPtr.Zero);
                return IntPtr.Zero;
            case WM_LBUTTONUP:
                return IntPtr.Zero;
            case 0x0312: // WM_HOTKEY
                Toggle();
                return IntPtr.Zero;
            case WM_RBUTTONUP:
                ShowMenu();
                return IntPtr.Zero;
            case WM_TRAYICON:
                if ((uint)lParam.ToInt64() == WM_RBUTTONUP) ShowMenu();
                else if ((uint)lParam.ToInt64() == WM_LBUTTONUP) Toggle();
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
                return IntPtr.Zero;
            case 0x0218: // WM_POWERBROADCAST
                RefreshBattery();
                return new IntPtr(1);
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
        _renderer.Draw(_surface.Canvas, _scale, (float)_width.Value, (float)_height.Value, _settings.Detached, _battery, _clock.Elapsed.TotalSeconds);
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
        _battery = BatteryStatus.Read();
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

    private void ShowMenu()
    {
        _menuOpen = true;
        KillTimer(_hwnd, (UIntPtr)CollapseTimer);
        IntPtr menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, MF_STRING, (UIntPtr)1, _expanded ? "Collapse Xylo" : "Expand Xylo");
            AppendMenu(menu, MF_STRING, (UIntPtr)3, _settings.Detached ? "Configuration: Attach to top" : "Configuration: Float below top");
            AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, "");
            AppendMenu(menu, MF_STRING, (UIntPtr)2, "Quit Xylo");
            GetCursorPos(out var cursor);
            SetForegroundWindow(_hwnd);
            uint command = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD,
                cursor.X, cursor.Y, 0, _hwnd, IntPtr.Zero);
            if (command == 1) Toggle();
            else if (command == 2) DestroyWindow(_hwnd);
            else if (command == 3)
            {
                bool previous = _settings.Detached;
                _settings.Detached = !previous;
                try { _settings.Save(); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    _settings.Detached = previous;
                    MessageBox(_hwnd, "Could not save Xylo configuration.", "Xylo", 0x10);
                }
                _y = _settings.Detached ? (int)(12 * _scale) : 0;
                _dirty = true;
            }
            PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            DestroyMenu(menu);
            _menuOpen = false;
            GetCursorPos(out var cursor);
            if (!IsInsidePill((cursor.X - _x) / _scale, (cursor.Y - _y) / _scale))
                SetTimer(_hwnd, (UIntPtr)CollapseTimer, 180, IntPtr.Zero);
        }
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
        if (_trayAdded) Shell_NotifyIcon(NIM_DELETE, ref _tray);
        if (IsWindow(_hwnd)) DestroyWindow(_hwnd);
        ReleaseSurface();
        _renderer.Dispose();
    }
}
