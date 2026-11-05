# Claude Code approvals

Xylo supports Claude Code `PermissionRequest`, `UserPromptSubmit`, `Stop`, and `SessionEnd` hooks. It presents the complete request in the notch; Allow permits, Deny rejects, and Cancel leaves the decision to Claude Code. All pages must be visited before allowing. If Xylo is unavailable or the request times out, the hook returns no decision. The `PermissionRequest` event does not cover every sandbox network prompt; see the [Claude Code hook reference](https://code.claude.com/docs/en/hooks#permissionrequest).

Merge these entries into the existing `hooks` object in `~/.claude/settings.json` or a project's `.claude/settings.json`, replacing the example path with the absolute path to this checkout. Python must be available as `python`. Preserve any existing hooks.

```json
{
  "hooks": {
    "PermissionRequest": [{"matcher": "*", "hooks": [{"type": "command", "command": "python \"E:/path/to/xylo/integrations/claude/xylo_hook.py\"", "timeout": 70}]}],
    "UserPromptSubmit": [{"hooks": [{"type": "command", "command": "python \"E:/path/to/xylo/integrations/claude/xylo_hook.py\"", "timeout": 3}]}],
    "Stop": [{"hooks": [{"type": "command", "command": "python \"E:/path/to/xylo/integrations/claude/xylo_hook.py\"", "timeout": 3}]}],
    "SessionEnd": [{"hooks": [{"type": "command", "command": "python \"E:/path/to/xylo/integrations/claude/xylo_hook.py\"", "timeout": 3}]}]
  }
}
```

Claude Code workspace trust and managed policy can affect whether hooks run. Activity is inferred from lifecycle hooks and can become stale if the agent terminates without a final event.
