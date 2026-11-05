# Xylo

Xylo is a Dynamic Island for Windows: a compact desktop notch being developed for AI agent activity, notifications, and quick clipboard access.

The desktop prototype includes media, lyrics, battery status, optional clipboard and notification previews, and agent approval adapters.

## Available now

- Native topmost notch attached to the primary display's top edge, without taking keyboard focus on hover.
- Hover to expand; move away to collapse after a brief grace period.
- Composition-paced spring animation, fading content, and transparent hit testing.
- Circular battery percentage indicator with a moving charge highlight; unavailable battery readings are hidden.
- Optional battery alerts for low or critical charge and power connection changes.
- Wi-Fi and Bluetooth connection alerts, configurable in the notch. Device names and Wi-Fi SSIDs depend on Windows permissions and device support.
- Volume up/down HUD with a level bar using Windows Core Audio events, configurable in the notch.
- Optional brightness HUD using Windows display events; availability depends on the display and driver.
- The weather view shows the next four hourly forecasts when weather is enabled. Forecasts use the location estimated from the public IP address.
- Task due dates and times are adjusted using controls in the notch; time changes in 15-minute steps.
- Notification previews show a WhatsApp or call badge when Windows identifies WhatsApp and supplies matching call text. WhatsApp Web notifications may appear under the browser without enough information to identify their origin or call type.
- Minimal animated eyes blink periodically without a continuously running animation loop.
- Displays the active Windows media session (including Spotify) with cover art, previous, play/pause, next and a progress bar in the expanded notch. Click the bar to seek when the media app supports it.
- Shows synchronized lyrics beside the player when enabled. Lyrics are fetched from LRCLIB using track metadata; no Spotify login is required. Unsynchronized lyrics show their first lines. Availability depends on the source and available screen width.
- Hover over the notch to see today's date and time, then choose Agent, Weather, To-do, Music, Clipboard, or Focus to switch views. The last selected view remains active when the notch closes and reopens; only Music shows the current media in the compact notch. Manage tasks, clipboard history, focus timer and weather directly from their views; approval requests remain visible regardless of the selected view.
- The Music view has a lyrics on/off button. Agent completion notices require the corresponding agent hooks to report both start and completion.
- Lyrics can be disabled in the notch controls to stop lyric requests while keeping media controls available.
- Optional clipboard history in the notch controls: enable explicitly, then select a recent text item to copy it. History holds up to 20 entries in memory and is cleared on exit or when disabled. Clipboard entries may contain sensitive information.
- Optional Windows notification previews can be enabled from the notch controls after granting Windows access. Availability depends on Windows permission and package identity; previews mirror content supplied by the originating app and may contain sensitive information.
- Focus, short break and long break timers, with pause/resume controls inside the notch.
- Optional weather in the notch. Enabling it sends your IP to ipwho.is to estimate location and sends approximate coordinates to Open-Meteo; no requests are made until enabled.
- Local task list with an editor inside the notch, completion, deletion, and optional due date and time.
- Optional [Codex](integrations/codex/README.md) and [Claude Code](integrations/claude/README.md) approval integrations, with an explicit in-notch review and safe fallback to the original agent prompt.
- Optional [Cursor activity and shell review](integrations/cursor/README.md); shell review is a separately enabled policy that reviews each matching command.
- Optional [OpenCode activity and approval notices](integrations/opencode/README.md); permission decisions are made in OpenCode.
- Optional [Antigravity activity and tool review](integrations/antigravity/README.md); the tool-review policy applies to every matching call and has only been tested with synthetic hook inputs.
- The system tray icon opens or collapses Xylo; right-click opens its controls inside the notch.
- The controls switch between an attached notch and a floating island; the choice is saved locally.
- `Ctrl+Alt+X` global toggle shortcut, when available.
- DPI-aware rendering and surface recreation after display changes.
- Single-instance startup and message-driven idle behavior: no continuous rendering when settled.

## Build and run

Requires Windows 10/11 x64 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet build src/Xylo/Xylo.csproj -c Release
dotnet run --project src/Xylo/Xylo.csproj -c Release --no-build
```

Right-click the notch or tray icon, or click the three dots in the expanded notch, to open its controls. Use **Next** in Controls to reach **Quit Xylo**. The notch currently uses the top center of the primary display; multi-monitor selection is planned.

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
- Clipboard search and pinned snippets.
- Configurable notch appearance, placement, and notification priorities.

Agent integrations differ in the events and approval controls they expose.

## Technical direction

Xylo adapts the native C# / SkiaSharp / Win32 foundation of [WinIsland](https://github.com/zHeuzy2/WinIsland). The Win32 declarations and spring integrator are adapted from upstream; the desktop shell and renderer are tailored to Xylo. See [third-party notices](third-party/README.md).

The initial memory target is **at most 50 MB at idle**, including Xylo helper processes. A short local run with media and lyric lookup active measured approximately **74 MiB working set** and **19 MiB private bytes**, so the working-set target is not yet met. See [performance notes](docs/performance.md).

## Development status

This is an early desktop prototype, not a complete agent integration product. There are no background agent watchers, camera services, weather requests, or automatic updater in this version. GitHub Actions builds a Windows folder artifact; GUI smoke testing runs locally in an interactive Windows session.

## License

[MIT](LICENSE).
