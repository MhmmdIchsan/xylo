"""Integration test for a synthetic approval; Xylo must already be running."""
import ctypes
import json
import pathlib
import subprocess
import sys
import time

user32 = ctypes.windll.user32
user32.FindWindowW.restype = ctypes.c_void_p
root = pathlib.Path(__file__).resolve().parents[1]
agent = sys.argv[1] if len(sys.argv) > 1 else "codex"
hook = root / "integrations" / agent / "xylo_hook.py"
event = ({"hook_event_name": "beforeShellExecution", "conversation_id": "xylo-integration-test",
          "cwd": "synthetic-test", "command": "echo xylo-test", "sandbox": True}
         if agent == "cursor" else
         {"hook_event_name": "PermissionRequest", "session_id": "xylo-integration-test",
          "cwd": "synthetic-test", "tool_name": "Bash", "tool_input": {"command": "echo xylo-test"}})
window = None
for _ in range(30):
    window = user32.FindWindowW("Xylo.Desktop.Window", None)
    if window:
        break
    time.sleep(0.1)
if not window:
    raise SystemExit("Start Xylo before running the bridge integration test")

for choice, expected in [(6, "allow"), (7, "deny"), (2, None)]:
    process = subprocess.Popen([sys.executable, str(hook)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                               stderr=subprocess.PIPE, creationflags=subprocess.CREATE_NO_WINDOW)
    process.stdin.write(json.dumps(event).encode())
    process.stdin.close()
    time.sleep(0.5)
    user32.PostMessageW(ctypes.c_void_p(window), 0x202, 0, 0)
    dialog = None
    for _ in range(30):
        dialog = user32.FindWindowW("#32770", "Xylo — review agent request")
        if dialog:
            break
        time.sleep(0.1)
    if not dialog:
        process.kill()
        raise SystemExit("Review dialog did not appear")
    user32.PostMessageW(ctypes.c_void_p(dialog), 0x111, choice, 0)
    process.wait(timeout=5)
    output = process.stdout.read()
    if agent == "cursor":
        actual = json.loads(output)["permission"]
        assert actual == (expected or "ask"), (actual, expected)
    elif expected is None:
        assert not output, output
    else:
        actual = json.loads(output)["hookSpecificOutput"]["decision"]["behavior"]
        assert actual == expected, (actual, expected)
print("Synthetic allow, deny, and defer approval flows passed.")
