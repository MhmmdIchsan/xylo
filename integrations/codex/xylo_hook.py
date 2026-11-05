"""Forward Codex permission requests to Xylo; safely defer if Xylo is unavailable."""
import json
import os
import sys


def is_process_ancestor(target_pid):
    """Only associate an editor window when its owner is an ancestor of this hook."""
    if os.name != "nt" or not target_pid:
        return False
    import ctypes
    from ctypes import wintypes
    class ProcessEntry(ctypes.Structure):
        _fields_ = [("dwSize", wintypes.DWORD), ("cntUsage", wintypes.DWORD),
                    ("th32ProcessID", wintypes.DWORD), ("th32DefaultHeapID", ctypes.c_size_t),
                    ("th32ModuleID", wintypes.DWORD), ("cntThreads", wintypes.DWORD),
                    ("th32ParentProcessID", wintypes.DWORD), ("pcPriClassBase", wintypes.LONG),
                    ("dwFlags", wintypes.DWORD), ("szExeFile", wintypes.WCHAR * 260)]
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.CreateToolhelp32Snapshot.argtypes = [wintypes.DWORD, wintypes.DWORD]
    kernel.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
    kernel.Process32FirstW.argtypes = [wintypes.HANDLE, ctypes.POINTER(ProcessEntry)]
    kernel.Process32NextW.argtypes = [wintypes.HANDLE, ctypes.POINTER(ProcessEntry)]
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    snapshot = kernel.CreateToolhelp32Snapshot(2, 0)
    if snapshot == ctypes.c_void_p(-1).value:
        return False
    try:
        parents = {}
        entry = ProcessEntry()
        entry.dwSize = ctypes.sizeof(entry)
        available = kernel.Process32FirstW(snapshot, ctypes.byref(entry))
        while available:
            parents[entry.th32ProcessID] = entry.th32ParentProcessID
            available = kernel.Process32NextW(snapshot, ctypes.byref(entry))
        current = os.getpid()
        seen = set()
        while current and current not in seen:
            if current == target_pid:
                return True
            seen.add(current)
            current = parents.get(current, 0)
        return False
    finally:
        kernel.CloseHandle(snapshot)


def foreground_agent_window():
    """Record only a recognized foreground terminal/editor at prompt submission."""
    if os.name != "nt":
        return {}
    try:
        import ctypes
        from ctypes import wintypes
        user = ctypes.WinDLL("user32", use_last_error=True)
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        user.GetForegroundWindow.restype = wintypes.HWND
        user.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
        kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        kernel.OpenProcess.restype = wintypes.HANDLE
        kernel.QueryFullProcessImageNameW.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
        kernel.CloseHandle.argtypes = [wintypes.HANDLE]
        hwnd = user.GetForegroundWindow()
        pid = wintypes.DWORD()
        if not hwnd or not user.GetWindowThreadProcessId(hwnd, ctypes.byref(pid)):
            return {}
        process = kernel.OpenProcess(0x1000, False, pid.value)
        if not process:
            return {}
        try:
            size = wintypes.DWORD(32768)
            path = ctypes.create_unicode_buffer(size.value)
            if not kernel.QueryFullProcessImageNameW(process, 0, path, ctypes.byref(size)):
                return {}
            executable = os.path.basename(path.value).casefold()
        finally:
            kernel.CloseHandle(process)
        allowed = {"code.exe", "code - insiders.exe", "cursor.exe", "windowsterminal.exe", "conhost.exe", "wezterm-gui.exe", "conemu64.exe"}
        return {"windowHandle": int(hwnd), "windowPid": pid.value} if executable in allowed and is_process_ancestor(pid.value) else {}
    except (OSError, ValueError, TypeError):
        return {}


def exchange(message):
    try:
        with open(r"\\.\pipe\xylo-agent", "r+b", buffering=0) as pipe:
            pipe.write(json.dumps(message, ensure_ascii=False).encode("utf-8") + b"\n")
            response = b""
            while len(response) < 4096 and not response.endswith(b"\n"):
                chunk = pipe.read(1)
                if not chunk:
                    break
                response += chunk
            return json.loads(response) if response else {}
    except (OSError, ValueError):
        return {}


def main(agent="Codex"):
    event = json.load(sys.stdin)
    kind = event.get("hook_event_name")
    if kind in ("PreToolUse", "PostToolUse"):
        tool_name = event.get("tool_name", "")
        if not isinstance(tool_name, str) or tool_name.rsplit(".", 1)[-1].rsplit("__", 1)[-1] not in ("request_user_input", "request_user_input_async"):
            return
        message = {"agent": agent, "event": "InputRequired" if kind == "PreToolUse" else "InputResolved",
                   "session": event.get("session_id", ""), "project": event.get("cwd", ""),
                   "requestId": str(event.get("tool_use_id") or "input")}
        exchange(message)
        return
    if kind not in ("PermissionRequest", "UserPromptSubmit", "Stop", "Interrupt", "SessionEnd"):
        return
    tool = event.get("tool_name", "") if kind == "PermissionRequest" else ""
    details = json.dumps(event.get("tool_input"), ensure_ascii=False, indent=2) if tool else ""
    if kind == "PermissionRequest" and (not tool or len(details) > 4096):
        return
    message = {
        "agent": agent, "event": kind, "session": event.get("session_id", ""),
        "tool": tool, "project": event.get("cwd", ""), "details": details,
    }
    if kind == "UserPromptSubmit":
        message.update(foreground_agent_window())
    result = exchange(message)
    if result:
        print(json.dumps(result))


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, TypeError):
        pass
