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
    time.sleep(1)
    scale = u.GetDpiForWindow(hwnd) / 96
    def click(x, y):
        u.SendMessageW(hwnd, 0x0202, 0, int(x * scale) | (int(y * scale) << 16))
        time.sleep(0.3)
    for x in (70, 160, 250, 340):
        click(x, 60)
    click(330, 21)  # Open controls
    time.sleep(1)
    click(230, 345) # Controls -> next page
    click(70, 95)   # Ask to enable weather
    time.sleep(0.8)
    click(350, 260) # Confirm inside Xylo
    time.sleep(0.8)
    click(70, 95)   # Disable again, using transient test settings
    click(70, 171)  # Enable connectivity alerts
    click(70, 171)  # Disable again
    click(70, 209)  # Enable volume HUD
    click(70, 209)  # Disable again
    click(70, 247)  # Enable brightness HUD
    click(70, 247)  # Disable again
    click(70, 285)  # Disable lyrics
    click(70, 285)  # Enable lyrics again
    click(160, 345) # Return to first page
    click(70, 95)   # Toggle position without changing the user's settings
    click(70, 247)  # Controls -> Tasks
    click(70, 95)   # Tasks -> Add task
    time.sleep(0.8)
    for char in "synthetic task":
        u.SendMessageW(hwnd, 0x0102, ord(char), 0)
    click(70, 150)
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
