"""Exercise validation and editing without changing the user's task list."""
import ctypes
import pathlib
import subprocess
import time

u = ctypes.windll.user32
u.FindWindowW.restype = ctypes.c_void_p
u.GetDlgItem.restype = ctypes.c_void_p
u.SendMessageW.argtypes = [ctypes.c_void_p, ctypes.c_uint, ctypes.c_size_t, ctypes.c_void_p]
root = pathlib.Path(__file__).resolve().parents[1]
exe = root / "src/Xylo/bin/Release/net8.0-windows10.0.19041.0/win-x64/Xylo.exe"
process = subprocess.Popen([str(exe), "--test-task-dialog"], creationflags=subprocess.CREATE_NO_WINDOW)


def wait_window(class_name, title):
    for _ in range(50):
        hwnd = u.FindWindowW(class_name, title)
        if hwnd:
            return hwnd
        time.sleep(0.1)
    raise RuntimeError(f"Window did not appear: {title}")


try:
    dialog = wait_window("Xylo.TaskDialog", "Edit task")
    title = u.GetDlgItem(ctypes.c_void_p(dialog), 10)
    due = u.GetDlgItem(ctypes.c_void_p(dialog), 11)
    assert title and due
    title_text = ctypes.create_unicode_buffer("synthetic task")
    due_text = ctypes.create_unicode_buffer("not a date")
    assert u.SendMessageW(title, 0x000C, 0, ctypes.cast(title_text, ctypes.c_void_p))
    assert u.SendMessageW(due, 0x000C, 0, ctypes.cast(due_text, ctypes.c_void_p))
    u.PostMessageW(ctypes.c_void_p(dialog), 0x111, 1, 0)
    error = wait_window("#32770", "Xylo tasks")
    u.PostMessageW(ctypes.c_void_p(error), 0x111, 1, 0)
    time.sleep(0.2)
    assert process.poll() is None, "Invalid date unexpectedly closed the dialog"
    due_text = ctypes.create_unicode_buffer("2030-12-24 13:30")
    assert u.SendMessageW(due, 0x000C, 0, ctypes.cast(due_text, ctypes.c_void_p))
    u.PostMessageW(ctypes.c_void_p(dialog), 0x111, 1, 0)
    assert process.wait(timeout=5) == 0, "Task edit did not preserve the expected values"
    print("Task dialog validation and edit passed.")
finally:
    if process.poll() is None:
        process.kill()
