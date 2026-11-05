# Antigravity activity and optional tool review

Add the following named hook to the existing `~/.gemini/config/hooks.json` or workspace `.agents/hooks.json`, replacing the path with this checkout. Python must be on PATH. Activity is approximate and prompt text is not forwarded.

```json
{
  "xylo-activity": {
    "PreInvocation": [{"type": "command", "command": "python \"E:/path/to/xylo/integrations/antigravity/xylo_hook.py\" PreInvocation", "timeout": 3}],
    "Stop": [{"type": "command", "command": "python \"E:/path/to/xylo/integrations/antigravity/xylo_hook.py\" Stop", "timeout": 3}]
  }
}
```

For optional tool review, add a separate named hook:

```json
{
  "xylo-review": {
    "PreToolUse": [{"matcher": "run_command", "hooks": [{"type": "command", "command": "python \"E:/path/to/xylo/integrations/antigravity/xylo_hook.py\" PreToolUse", "timeout": 70}]}]
  }
}
```

This review hook runs on **every matching tool call**, including those the agent's normal policy might allow. Xylo Allow returns `auto_approve`, Deny returns `deny`, and Cancel or unavailable Xylo returns `allow`, which delegates to the agent's remaining hooks and default policy. Explicit user-configured rules still apply. Review the full request before deciding. This adapter has been tested with synthetic hook inputs; a live Antigravity installation has not yet been tested. See the [Antigravity hook reference](https://antigravity.google/docs/hooks).
