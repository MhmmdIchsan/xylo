using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using static Xylo.Native;

namespace Xylo;

internal sealed class TaskDialog
{
    private const uint WmCommand = 0x0111, WmClose = 0x0010;
    private const uint ChildVisible = WS_CHILD | WS_VISIBLE;
    private static readonly WndProc Procedure = WindowProc;
    private static bool _registered;
    private static TaskDialog? _current;
    private IntPtr _window, _title, _due;
    private bool _done;
    private TaskItem? _result;

    public static TaskItem? Edit(IntPtr owner, TaskItem? item)
    {
        if (_current != null) return null;
        var dialog = new TaskDialog();
        _current = dialog;
        try { return dialog.Run(owner, item); }
        finally { if (IsWindow(dialog._window)) DestroyWindow(dialog._window); _current = null; }
    }

    private TaskItem? Run(IntPtr owner, TaskItem? item)
    {
        IntPtr instance = GetModuleHandle(null);
        if (!_registered)
        {
            var cls = new WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = Procedure,
                hInstance = instance, hCursor = LoadCursor(IntPtr.Zero, IDC_ARROW),
                hbrBackground = new IntPtr(6), lpszClassName = "Xylo.TaskDialog" };
            if (RegisterClassEx(ref cls) == 0) throw new Win32Exception();
            _registered = true;
        }
        int width = 430, height = 225;
        _window = CreateWindowEx(WS_EX_DLGMODALFRAME | WS_EX_TOPMOST, "Xylo.TaskDialog", item == null ? "Add task" : "Edit task",
            WS_CAPTION | WS_SYSMENU, (GetSystemMetrics(SM_CXSCREEN) - width) / 2, (GetSystemMetrics(SM_CYSCREEN) - height) / 2,
            width, height, owner, IntPtr.Zero, instance, IntPtr.Zero);
        if (_window == IntPtr.Zero) throw new Win32Exception();
        AddControl("STATIC", "Task", ChildVisible, 18, 14, 370, 20, 0);
        _title = AddControl("EDIT", item?.Title ?? "", ChildVisible | WS_BORDER | ES_AUTOHSCROLL | 0x10000, 18, 36, 375, 25, 10);
        AddControl("STATIC", "Due date (optional: YYYY-MM-DD or YYYY-MM-DD HH:mm)", ChildVisible, 18, 72, 380, 20, 0);
        _due = AddControl("EDIT", item?.Due?.ToString(item.HasTime ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            ChildVisible | WS_BORDER | ES_AUTOHSCROLL | 0x10000, 18, 95, 375, 25, 11);
        AddControl("BUTTON", "Save", ChildVisible | 0x10000 | 1, 225, 137, 80, 29, 1);
        AddControl("BUTTON", "Cancel", ChildVisible | 0x10000, 313, 137, 80, 29, 2);
        ShowWindow(_window, SW_SHOW);
        SetForegroundWindow(_window);
        SetFocus(_title);
        while (!_done)
        {
            if (!GetMessage(out var message, IntPtr.Zero, 0, 0)) { PostQuitMessage(0); break; }
            if (!IsDialogMessage(_window, ref message)) { TranslateMessage(ref message); DispatchMessage(ref message); }
        }
        if (IsWindow(_window)) DestroyWindow(_window);
        if (_result != null && item != null) _result.Done = item.Done;
        return _result;
    }

    private IntPtr AddControl(string kind, string text, uint style, int x, int y, int width, int height, int id)
    {
        IntPtr control = CreateWindowEx(0, kind, text, style, x, y, width, height, _window, new IntPtr(id), GetModuleHandle(null), IntPtr.Zero);
        if (control == IntPtr.Zero) throw new Win32Exception();
        SendMessage(control, WM_SETFONT, GetStockObject(DEFAULT_GUI_FONT), new IntPtr(1));
        return control;
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        var dialog = _current;
        if (dialog != null && message == WmCommand)
        {
            int id = (int)(wParam.ToInt64() & 0xffff);
            if (id == 1) { dialog.Save(); return IntPtr.Zero; }
            if (id == 2) { DestroyWindow(hwnd); dialog._done = true; return IntPtr.Zero; }
        }
        if (dialog != null && message == WmClose) { DestroyWindow(hwnd); dialog._done = true; return IntPtr.Zero; }
        if (dialog != null && message == WM_DESTROY) { dialog._done = true; return IntPtr.Zero; }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void Save()
    {
        string title = ReadText(_title).Trim();
        string dueText = ReadText(_due).Trim();
        if (title.Length is 0 or > 200) { MessageBox(_window, "Enter a task of 1–200 characters.", "Xylo tasks", 0x10); return; }
        DateTime? due = null;
        if (dueText.Length > 0)
        {
            if (!DateTime.TryParseExact(dueText, new[] { "yyyy-MM-dd", "yyyy-MM-dd HH:mm" }, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            {
                MessageBox(_window, "Use YYYY-MM-DD or YYYY-MM-DD HH:mm for the due date.", "Xylo tasks", 0x10);
                return;
            }
            due = parsed;
        }
        _result = new TaskItem { Title = title, Due = due, HasTime = dueText.Length > 10 };
        DestroyWindow(_window);
        _done = true;
    }

    private static string ReadText(IntPtr control)
    {
        int length = Math.Min(GetWindowTextLength(control), 2048);
        var text = new StringBuilder(length + 1);
        GetWindowText(control, text, text.Capacity);
        return text.ToString();
    }
}
