using System.Runtime.InteropServices;

namespace Xylo;

internal sealed class ClipboardHistory
{
    private const int Limit = 20, MaxCharacters = 4096;
    private readonly List<string> _items = new();
    public IReadOnlyList<string> Items => _items;

    public void Capture(IntPtr hwnd)
    {
        string? value = ReadText(hwnd);
        if (value != null) Add(value);
    }

    public static string? ReadText(IntPtr hwnd)
    {
        if (!Native.IsClipboardFormatAvailable(Native.CF_UNICODETEXT) || !Native.OpenClipboard(hwnd)) return null;
        try
        {
            IntPtr data = Native.GetClipboardData(Native.CF_UNICODETEXT);
            if (data == IntPtr.Zero) return null;
            nuint size = Native.GlobalSize(data);
            if (size == 0 || size > (MaxCharacters + 1) * 2) return null;
            IntPtr ptr = Native.GlobalLock(data);
            if (ptr == IntPtr.Zero) return null;
            try
            {
                int length = checked((int)size / 2);
                return Marshal.PtrToStringUni(ptr, length)?.TrimEnd('\0');
            }
            finally { Native.GlobalUnlock(data); }
        }
        finally { Native.CloseClipboard(); }
    }

    internal void Add(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxCharacters) return;
        _items.Remove(value);
        _items.Insert(0, value);
        if (_items.Count > Limit) _items.RemoveRange(Limit, _items.Count - Limit);
    }

    public bool Copy(IntPtr hwnd, int index)
    {
        if (index < 0 || index >= _items.Count || !Native.OpenClipboard(hwnd)) return false;
        IntPtr memory = IntPtr.Zero;
        try
        {
            string value = _items[index];
            memory = Native.GlobalAlloc(2, (nuint)((value.Length + 1) * 2));
            if (memory == IntPtr.Zero) return false;
            IntPtr ptr = Native.GlobalLock(memory);
            if (ptr == IntPtr.Zero) return false;
            try { Marshal.Copy((value + '\0').ToCharArray(), 0, ptr, value.Length + 1); }
            finally { Native.GlobalUnlock(memory); }
            if (!Native.EmptyClipboard() || Native.SetClipboardData(Native.CF_UNICODETEXT, memory) == IntPtr.Zero) return false;
            memory = IntPtr.Zero; // Windows owns it after SetClipboardData succeeds.
            return true;
        }
        finally
        {
            if (memory != IntPtr.Zero) Native.GlobalFree(memory);
            Native.CloseClipboard();
        }
    }

    public void Clear() => _items.Clear();
}
