"""Forward Codex permission requests to Xylo; safely defer if Xylo is unavailable."""
import json
import sys


def main(agent="Codex"):
    event = json.load(sys.stdin)
    kind = event.get("hook_event_name")
    if kind not in ("PermissionRequest", "UserPromptSubmit", "Stop", "SessionEnd"):
        return
    tool = event.get("tool_name", "") if kind == "PermissionRequest" else ""
    details = json.dumps(event.get("tool_input"), ensure_ascii=False, indent=2) if tool else ""
    if kind == "PermissionRequest" and (not tool or len(details) > 4096):
        return
    message = json.dumps({
        "agent": agent, "event": kind, "session": event.get("session_id", ""),
        "tool": tool, "project": event.get("cwd", ""), "details": details,
    }, ensure_ascii=False).encode("utf-8") + b"\n"
    try:
        with open(r"\\.\pipe\xylo-agent", "r+b", buffering=0) as pipe:
            pipe.write(message)
            response = b""
            while len(response) < 4096 and not response.endswith(b"\n"):
                chunk = pipe.read(1)
                if not chunk:
                    break
                response += chunk
            if response:
                result = json.loads(response)
                if result:
                    print(json.dumps(result))
    except (OSError, ValueError):
        pass


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, TypeError):
        pass
