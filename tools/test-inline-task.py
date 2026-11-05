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
        for _ in range(60):
            width = u.SendMessageW(hwnd, 0x8019, 0, 0)
            actual = u.SendMessageW(hwnd, 0x801A, 0, 0)
            height_error = u.SendMessageW(hwnd, 0x801B, 0, 0)
            if abs(width - actual) <= 1 and abs(height_error) <= 1:
                break
            time.sleep(0.05)
        else:
            raise AssertionError("Notch animation did not settle")
        canvas_x = (700 - width) / 2 + x - 20
        scale = u.SendMessageW(hwnd, 0x801C, 0, 0) / 1000
        u.SendMessageW(hwnd, 0x0202, 0, int(canvas_x * scale) | (int(y * scale) << 16))
        time.sleep(0.3)
    for x in (69, 119, 169, 219, 269, 319, 369):
        click(x, 60)
    width = u.SendMessageW(hwnd, 0x8019, 0, 0)
    click(width - 38, 21) # Turn on Do Not Disturb from the bell
    click(width - 38, 21) # Turn it off
    click(319, 60)  # Clipboard
    click(90, 195)  # Enable history
    click(90, 195)  # Disable and clear history
    click(369, 60)  # Focus
    click(90, 145)  # Start a focus preset
    click(90, 195)  # Pause
    click(250, 195) # Clear timer
    click(269, 60)  # Music
    click(350, 200) # Disable lyrics in Music
    click(350, 200) # Re-enable lyrics in Music
    width = u.SendMessageW(hwnd, 0x8019, 0, 0)
    click(width - 70, 21)  # Open controls at the right edge
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
    click(230, 345) # Appearance is on the third page
    click(70, 95)   # Appearance
    click(70, 95)   # Hide battery percentage
    click(70, 95)   # Restore it
    for _ in range(3):
        click(70, 171) # Cycle through size presets back to normal
    click(70, 247)  # Arrange tabs
    click(70, 133)  # Agent tab
    click(70, 95)   # Hide Agent
    click(70, 95)   # Show Agent
    click(70, 133)  # Move left
    click(70, 171)  # Move right
    click(60, 345)  # Back to tabs
    click(60, 345)  # Back to appearance
    click(60, 345)  # Back to controls
    click(70, 95)   # Toggle position without changing the user's settings
    click(70, 247)  # Controls -> Tasks
    click(70, 95)   # Tasks -> Add task
    time.sleep(0.8)
    u.SendMessageW(hwnd, 0x0100, 0x0D, 0)
    assert process.poll() is None, "Empty title unexpectedly saved"
    for char in "synthetic task":
        u.SendMessageW(hwnd, 0x0102, ord(char), 0)
    click(272, 150) # Tomorrow
    click(160, 185) # Hour +1
    click(300, 185) # Minute +15
    u.SendMessageW(hwnd, 0x0100, 0x0D, 0)
    assert process.wait(timeout=5) == 0, "Task editor did not save the expected values"
    print("Inline task input, validation and save passed.")
finally:
    if process.poll() is None:
        process.kill()
