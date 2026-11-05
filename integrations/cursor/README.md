# Cursor activity and optional shell review

Add the following to a Cursor `~/.cursor/hooks.json` or project `.cursor/hooks.json`, preserving existing hooks and replacing the paths with this checkout. Python must be on PATH. The activity hooks report an approximate working/finished state without sending prompt contents to Xylo.

```json
{
  "version": 1,
  "hooks": {
    "beforeSubmitPrompt": [{"command": "python \"E:/path/to/xylo/integrations/cursor/xylo_hook.py\"", "timeout": 3}],
    "stop": [{"command": "python \"E:/path/to/xylo/integrations/cursor/xylo_hook.py\"", "timeout": 3}],
    "sessionEnd": [{"command": "python \"E:/path/to/xylo/integrations/cursor/xylo_hook.py\"", "timeout": 3}]
  }
}
```

To review shell commands in Xylo, additionally add this entry to the same `hooks` object:

```json
"beforeShellExecution": [{"command": "python \"E:/path/to/xylo/integrations/cursor/xylo_hook.py\"", "timeout": 70, "failClosed": true}]
```

This optional policy asks Xylo to review **every matching shell command**, including commands Cursor might otherwise allow. Xylo Yes/No maps to allow/deny; Cancel or an unavailable Xylo returns `ask`, so Cursor handles the prompt. A hook crash or timeout blocks the command because `failClosed` is enabled. Cursor merges decisions from other hooks and policies, which can still deny or ask. It is not an observer of Cursor's existing approval dialog and does not cover MCP or other tool requests. See the [Cursor hook reference](https://cursor.com/docs/hooks).
