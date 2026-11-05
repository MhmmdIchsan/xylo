"""Cursor activity and optional shell approvals through Xylo."""
import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / "codex"))
from xylo_hook import exchange


def main():
    event = json.load(sys.stdin)
    kind = event.get("hook_event_name", "")
    names = {"beforeSubmitPrompt": "UserPromptSubmit", "stop": "Stop", "sessionEnd": "SessionEnd",
             "beforeShellExecution": "PermissionRequest"}
    mapped = names.get(kind)
    if not mapped:
        print("{}")
        return
    roots = event.get("workspace_roots") or []
    project = event.get("cwd") or (roots[0] if roots else "")
    details = json.dumps({"command": event.get("command"), "cwd": project,
                          "sandbox": event.get("sandbox")}, ensure_ascii=False, indent=2) if mapped == "PermissionRequest" else ""
    response = exchange({"agent": "Cursor", "event": mapped, "session": event.get("conversation_id", ""),
                         "project": project, "tool": "Shell" if details else "", "details": details})
    if mapped == "PermissionRequest":
        decision = response.get("hookSpecificOutput", {}).get("decision", {}).get("behavior", "ask")
        print(json.dumps({"permission": decision if decision in ("allow", "deny") else "ask"}))
    elif kind == "beforeSubmitPrompt":
        print(json.dumps({"continue": True}))
    else:
        print("{}")


try:
    main()
except Exception:
    # Cursor's failClosed option blocks on exit 2, including malformed hook input.
    sys.exit(2)
