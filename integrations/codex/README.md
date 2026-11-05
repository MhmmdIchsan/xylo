# Codex approvals

Xylo can review Codex `PermissionRequest` hooks through a named pipe restricted to the current Windows user. Xylo shows the complete tool input in a confirmation dialog; **Yes** allows, **No** denies, and **Cancel** defers to Codex's normal approval flow. If Xylo is not running, the pipe is busy, the request is too large, or no decision arrives within 60 seconds, the hook returns no decision and Codex handles approval normally. Multiple pending requests are presented one at a time. Request contents stay in memory and are not logged by Xylo.

To enable this for a Codex configuration layer, merge the following entry into that layer's existing `hooks.json`; do not overwrite other hooks. Replace the example path with the absolute path to `xylo_hook.py` in your checkout and ensure Python is available as `python`.

```json
{
  "hooks": {
    "PermissionRequest": [{
      "matcher": "*",
      "hooks": [{
        "type": "command",
        "command": "python \"E:/path/to/xylo/integrations/codex/xylo_hook.py\"",
        "timeout": 70
      }]
    }],
    "UserPromptSubmit": [{"hooks": [{"type": "command", "command": "python \"E:/path/to/xylo/integrations/codex/xylo_hook.py\"", "timeout": 3}]}],
    "Stop": [{"hooks": [{"type": "command", "command": "python \"E:/path/to/xylo/integrations/codex/xylo_hook.py\"", "timeout": 3}]}],
    "SessionEnd": [{"hooks": [{"type": "command", "command": "python \"E:/path/to/xylo/integrations/codex/xylo_hook.py\"", "timeout": 3}]}]
  }
}
```

Codex requires new non-managed hooks to be reviewed and trusted in `/hooks` or the app's hook settings. Hook availability and approval coverage depend on the installed Codex version and policy. See the [official Codex hooks documentation](https://learn.chatgpt.com/docs/hooks).

UserPromptSubmit and Stop provide an approximate working/finished indicator; SessionEnd removes the session. These are hook events, not a guarantee that a process is still alive. [Claude Code](../claude/README.md), [Cursor](../cursor/README.md), [OpenCode](../opencode/README.md), and [Antigravity](../antigravity/README.md) have separate integrations.
