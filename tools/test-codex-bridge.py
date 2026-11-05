"""Integration test for a synthetic approval; Xylo must already be running."""
import ctypes
import json
import os
import pathlib
import subprocess
import sys
import time

user32 = ctypes.windll.user32
user32.FindWindowW.restype = ctypes.c_void_p
user32.GetDpiForWindow.argtypes = [ctypes.c_void_p]
root = pathlib.Path(__file__).resolve().parents[1]
agent = sys.argv[1] if len(sys.argv) > 1 else "codex"
hook = root / "integrations" / agent / "xylo_hook.py"
event = ({"conversationId": "xylo-integration-test", "workspacePaths": ["synthetic-test"],
          "toolCall": {"name": "run_command", "args": {"CommandLine": "echo xylo-test"}}}
         if agent == "antigravity" else
         {"hook_event_name": "beforeShellExecution", "conversation_id": "xylo-integration-test",
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
time.sleep(1)  # The window can appear before the agent pipe starts listening.

def notch_scale():
    settings_path = pathlib.Path(os.environ["APPDATA"]) / "Xylo" / "settings.json"
    try:
        preset = json.loads(settings_path.read_text(encoding="utf-8-sig")).get("SizePreset", 1)
    except (OSError, ValueError):
        preset = 1
    return user32.GetDpiForWindow(ctypes.c_void_p(window)) / 96 * {0: 0.9, 1: 1.0, 2: 1.1}.get(preset, 1.0)

for choice, expected in [(6, "allow"), (7, "deny"), (2, None)]:
    command = [sys.executable, str(hook)] + (["PreToolUse"] if agent == "antigravity" else [])
    process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                               stderr=subprocess.PIPE, creationflags=subprocess.CREATE_NO_WINDOW)
    process.stdin.write(json.dumps(event).encode())
    process.stdin.close()
    time.sleep(0.5)
    user32.PostMessageW(ctypes.c_void_p(window), 0x202, 0, 0)
    time.sleep(1)
    scale = notch_scale()
    x = int(({6: 336, 7: 210, 2: 94}[choice] + 130) * scale)
    y = int(373 * scale)
    user32.PostMessageW(ctypes.c_void_p(window), 0x202, 0, x | (y << 16))
    try:
        process.wait(timeout=5)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait()
        raise
    output = process.stdout.read()
    if agent == "antigravity":
        actual = json.loads(output)["decision"]
        assert actual == {"allow": "auto_approve", "deny": "deny", None: "allow"}[expected], (actual, expected)
    elif agent == "cursor":
        actual = json.loads(output)["permission"]
        assert actual == (expected or "ask"), (actual, expected)
    elif expected is None:
        assert not output, output
    else:
        actual = json.loads(output)["hookSpecificOutput"]["decision"]["behavior"]
        assert actual == expected, (actual, expected)
print("Synthetic allow, deny, and defer approval flows passed.")

if agent == "codex":
    event["tool_input"]["command"] = "echo " + "a" * 1800
    process = subprocess.Popen([sys.executable, str(hook)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                               stderr=subprocess.PIPE, creationflags=subprocess.CREATE_NO_WINDOW)
    try:
        process.stdin.write(json.dumps(event).encode())
        process.stdin.close()
        time.sleep(0.5)
        user32.PostMessageW(ctypes.c_void_p(window), 0x202, 0, 0)
        time.sleep(1)
        scale = notch_scale()
        def click(x, y):
            user32.PostMessageW(ctypes.c_void_p(window), 0x202, 0, int((x + 130) * scale) | (int(y * scale) << 16))
            time.sleep(0.05)
        click(336, 373)
        assert process.poll() is None, "Allow was accepted before all pages were reviewed"
        for _ in range(20):
            click(165, 320)
        click(336, 373)
        process.wait(timeout=5)
        assert json.loads(process.stdout.read())["hookSpecificOutput"]["decision"]["behavior"] == "allow"
        print("Multi-page review requires all pages before allow.")
    finally:
        if process.poll() is None:
            process.kill()
