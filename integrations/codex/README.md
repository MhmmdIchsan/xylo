# Codex approvals

Xylo can review Codex `PermissionRequest` hooks through a named pipe restricted to the current Windows user. Hover over the approval status to review its complete input in the notch; **Allow** allows, **Deny** denies, and **Cancel** defers to Codex's normal approval flow. All pages must be visited before Allow is enabled. Non-ASCII and control characters are displayed as Unicode escapes so invisible or unsupported characters remain visible. If Xylo is not running, the pipe is busy, the request is too large, or no decision arrives within 60 seconds, the hook returns no decision and Codex handles approval normally. Multiple pending requests are presented one at a time. Request contents stay in memory and are not logged by Xylo.

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
    "Interrupt": [{"hooks": [{"type": "command", "command": "python \"E:/path/to/xylo/integrations/codex/xylo_hook.py\"", "timeout": 3}]}],
    "PreToolUse": [{"matcher": ".*request_user_input.*", "hooks": [{"type": "command", "command": "python \"E:/path/to/xylo/integrations/codex/xylo_hook.py\"", "timeout": 3}]}],
    "PostToolUse": [{"matcher": ".*request_user_input.*", "hooks": [{"type": "command", "command": "python \"E:/path/to/xylo/integrations/codex/xylo_hook.py\"", "timeout": 3}]}],
    "SessionEnd": [{"hooks": [{"type": "command", "command": "python \"E:/path/to/xylo/integrations/codex/xylo_hook.py\"", "timeout": 3}]}]
  }
}
```

Codex requires new non-managed hooks to be reviewed and trusted in `/hooks` or the app's hook settings. Hook availability and approval coverage depend on the installed Codex version and policy. See the [official Codex hooks documentation](https://learn.chatgpt.com/docs/hooks).

UserPromptSubmit, Stop and Interrupt provide an approximate working/finished/interrupted indicator; SessionEnd removes the session. These are hook events, not a guarantee that a process is still alive. Interrupt requires a supported local Codex session and a trusted hook. [Claude Code](../claude/README.md), [Cursor](../cursor/README.md), [OpenCode](../opencode/README.md), and [Antigravity](../antigravity/README.md) have separate integrations.

For Codex tool paths that emit PreToolUse and PostToolUse for `request_user_input`, Xylo shows `needs input` until the matching call resolves. Click that status to return to the recorded terminal/editor; answers stay in Codex. Xylo records a window only when a recognized foreground editor or terminal owns an ancestor process of the hook. It validates the window and process again before restoring it; if no window can be identified, open Codex directly. Some specialized tool paths and cloud-orchestrated sessions do not run these hooks, so Xylo cannot detect every question. No prompt or answer text is sent to Xylo for this status.
