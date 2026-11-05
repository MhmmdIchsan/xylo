# OpenCode activity and approval notices

Copy `xylo.js` into `~/.config/opencode/plugins/` for all projects, or into `.opencode/plugins/` for one project. Restart OpenCode. The plugin forwards session status and permission events to the current-user Xylo named pipe; it does not send prompt text or tool arguments.

When approval is requested, Xylo displays the agent, permission name and project. **Respond in the original OpenCode session.** The plugin deliberately does not send approval decisions: OpenCode has multiple permission APIs and [external replies can fail to resolve interactive TUI requests](https://github.com/anomalyco/opencode/issues/36835) on some versions. Notices expire after two minutes if no resolution event arrives, and activity is approximate.

See [OpenCode plugins](https://opencode.ai/docs/plugins/) and its [server API](https://opencode.ai/docs/server/).
