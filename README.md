# Xylo

Xylo is a Dynamic Island for Windows: a compact desktop notch being developed for AI agent activity, notifications, and quick clipboard access.

The first desktop prototype is implemented. It currently provides the notch shell; agent integrations, notification previews, and clipboard history are not implemented yet.

## Available now

- Native topmost notch attached to the primary display's top edge, without taking keyboard focus on hover.
- Hover to expand; move away to collapse after a brief grace period.
- Composition-paced spring animation, fading content, and transparent hit testing.
- Circular battery percentage indicator with a moving charge highlight; unavailable battery readings are hidden.
- Minimal animated eyes blink periodically without a continuously running animation loop.
- System tray menu to expand/collapse or quit.
- Configuration in the right-click menu switches between an attached notch and a floating island; the choice is saved locally.
- `Ctrl+Alt+X` global toggle shortcut, when available.
- DPI-aware rendering and surface recreation after display changes.
- Single-instance startup and message-driven idle behavior: no continuous rendering when settled.

## Build and run

Requires Windows 10/11 x64 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet build src/Xylo/Xylo.csproj -c Release
dotnet run --project src/Xylo/Xylo.csproj -c Release --no-build
```

Right-click the notch or tray icon and choose **Quit Xylo** to exit. The notch currently uses the top center of the primary display. Multi-monitor selection and persistent settings are planned.

Create a framework-dependent folder build (requires the .NET 8 x64 runtime on the target computer):

```powershell
dotnet publish src/Xylo/Xylo.csproj -c Release --self-contained false -o artifacts/xylo
```

The automated Windows smoke test runs ten expand/collapse transitions, checks for continued rendering at idle and GDI resource growth, and reports memory and CPU measurements:

```powershell
powershell -File tools/smoke-test.ps1
```

Quit any running instance before testing. The test briefly shows the notch and exits automatically; its report is written to `%TEMP%/xylo-smoke-test.json`. Startup errors are written to `%LOCALAPPDATA%/Xylo/last-error.txt`.

## Planned features

- AI agent activity: working, waiting for input, completed, and failed states across multiple sessions.
- Approval requests in the notch, with approve/reject actions where the agent integration supports them.
- Windows notification previews, including messaging apps when their notifications expose message content.
- Text clipboard history with search and pinned snippets.
- Configurable notch appearance, placement, and notification priorities.

Planned agent integrations include Codex, Claude Code, OpenCode, Cursor, and Antigravity. Supported capabilities will depend on each agent's available integration interfaces.

## Technical direction

Xylo adapts the native C# / SkiaSharp / Win32 foundation of [WinIsland](https://github.com/zHeuzy2/WinIsland). The Win32 declarations and spring integrator are adapted from upstream; the desktop shell and renderer are tailored to Xylo. See [third-party notices](third-party/README.md).

The initial memory target is **at most 50 MB at idle**, including Xylo helper processes. Short local smoke tests of the desktop shell measured approximately **33 MiB working set** and **10 MiB private bytes**, with no additional helper processes. These are preliminary measurements, not a long-running or fully integrated product guarantee. See [performance notes](docs/performance.md).

## Development status

This is an early desktop prototype, not a complete agent integration product. There are no background agent watchers, camera services, weather requests, or automatic updater in this version. GitHub Actions builds a Windows folder artifact; GUI smoke testing runs locally in an interactive Windows session.

## License

[MIT](LICENSE).
