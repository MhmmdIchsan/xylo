# Xylo

Xylo is a planned Dynamic Island for Windows: a compact desktop notch for AI agent activity, notifications, and quick clipboard access.

The project is in its initial setup stage. There is no application implementation yet.

## Planned features

- AI agent activity: working, waiting for input, completed, and failed states across multiple sessions.
- Approval requests in the notch, with approve/reject actions where the agent integration supports them.
- Windows notification previews, including messaging apps when their notifications expose message content.
- Text clipboard history with search and pinned snippets.
- Configurable notch appearance, placement, and notification priorities.

Planned agent integrations include Codex, Claude Code, OpenCode, Cursor, and Antigravity. Supported capabilities will depend on each agent's available integration interfaces.

## Technical direction

Xylo plans to adapt the native C# / SkiaSharp / Win32 foundation of [WinIsland](https://github.com/zHeuzy2/WinIsland). Upstream code has not been imported. Applicable copyright notices and license terms will be preserved when code is adapted.

The initial memory target is **at most 50 MB at idle**, including Xylo helper processes. This is an unverified engineering target, not a measured result. Event-driven integrations, bounded caches, and rendering only when necessary will guide implementation.

## Development status

This repository currently contains project documentation, a license, and Git ignore rules. Build and run instructions will be added with the first application implementation.

## License

[MIT](LICENSE).
