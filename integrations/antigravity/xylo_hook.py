"""Antigravity status and optional tool review via Xylo."""
import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / "codex"))
from xylo_hook import exchange


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else ""
    event = json.load(sys.stdin)
    kinds = {"PreInvocation": "UserPromptSubmit", "Stop": "Stop", "PreToolUse": "PermissionRequest"}
    kind = kinds.get(mode)
    if not kind:
        print("{}")
        return
    roots = event.get("workspacePaths") or []
    tool = event.get("toolCall") or {}
    tool_name = tool.get("name", "") if kind == "PermissionRequest" else ""
    details = json.dumps(tool.get("args"), ensure_ascii=False, indent=2) if tool_name else ""
    if kind == "PermissionRequest" and not tool_name:
        print(json.dumps({"decision": "ask"}))
        return
    response = exchange({"agent": "Antigravity", "event": kind, "session": event.get("conversationId", ""),
                         "project": roots[0] if roots else "", "tool": tool_name, "details": details})
    if kind == "PermissionRequest":
        decision = response.get("hookSpecificOutput", {}).get("decision", {}).get("behavior")
        print(json.dumps({"decision": {"allow": "auto_approve", "deny": "deny"}.get(decision, "allow")}))
    else:
        print("{}")


try:
    main()
except Exception:
    # Malformed input is blocked rather than silently authorizing a tool.
    print(json.dumps({"decision": "deny"}))
    sys.exit(2)
