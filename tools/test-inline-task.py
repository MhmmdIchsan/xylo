"""Exercise task input inside the notch without touching the user's task list."""
import ctypes
import pathlib
import subprocess
import time

u = ctypes.windll.user32
u.FindWindowW.restype = ctypes.c_void_p
u.GetDpiForWindow.argtypes = [ctypes.c_void_p]
u.SendMessageW.argtypes = [ctypes.c_void_p, ctypes.c_uint, ctypes.c_size_t, ctypes.c_ssize_t]
root = pathlib.Path(__file__).resolve().parents[1]
exe = root / "src/Xylo/bin/Release/net8.0-windows10.0.19041.0/win-x64/Xylo.exe"
process = subprocess.Popen([str(exe), "--test-inline-task"], creationflags=subprocess.CREATE_NO_WINDOW)
try:
    hwnd = None
    for _ in range(50):
        hwnd = u.FindWindowW("Xylo.Desktop.Window", None)
        if hwnd:
            break
        time.sleep(0.1)
    assert hwnd, "Notch did not open"
    time.sleep(0.8)
    for char in "synthetic task":
        u.SendMessageW(hwnd, 0x0102, ord(char), 0)
    scale = u.GetDpiForWindow(hwnd) / 96
    x, y = int(70 * scale), int(150 * scale)
    u.SendMessageW(hwnd, 0x0202, 0, x | (y << 16))
    for char in "invalid":
        u.SendMessageW(hwnd, 0x0102, ord(char), 0)
    u.SendMessageW(hwnd, 0x0100, 0x0D, 0)
    assert process.poll() is None, "Invalid date unexpectedly saved"
    for _ in "invalid":
        u.SendMessageW(hwnd, 0x0100, 0x08, 0)
    for char in "2030-12-24 13:30":
        u.SendMessageW(hwnd, 0x0102, ord(char), 0)
    u.SendMessageW(hwnd, 0x0100, 0x0D, 0)
    assert process.wait(timeout=5) == 0, "Task editor did not save the expected values"
    print("Inline task input, validation and save passed.")
finally:
    if process.poll() is None:
        process.kill()
