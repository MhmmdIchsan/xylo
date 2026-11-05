"""Forward Codex permission requests to Xylo; safely defer if Xylo is unavailable."""
import json
import sys


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
    if kind not in ("PermissionRequest", "UserPromptSubmit", "Stop", "SessionEnd"):
        return
    tool = event.get("tool_name", "") if kind == "PermissionRequest" else ""
    details = json.dumps(event.get("tool_input"), ensure_ascii=False, indent=2) if tool else ""
    if kind == "PermissionRequest" and (not tool or len(details) > 4096):
        return
    message = {
        "agent": agent, "event": kind, "session": event.get("session_id", ""),
        "tool": tool, "project": event.get("cwd", ""), "details": details,
    }
    result = exchange(message)
    if result:
        print(json.dumps(result))


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, TypeError):
        pass
