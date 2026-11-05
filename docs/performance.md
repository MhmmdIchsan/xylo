# Desktop shell performance baseline

Measured locally on Windows x64 on November 5, 2026, using a Release framework-dependent build, .NET 8, and SkiaSharp 3.119.4.

Run `tools/smoke-test.ps1` from an interactive Windows session with no other Xylo instance running. The script builds, launches the shell, triggers ten expand/collapse transitions, samples an approximately two-second idle interval, writes `%TEMP%/xylo-smoke-test.json`, and exits the app.

Two local runs measured:

| Measurement | Observed |
| --- | --- |
| Working set after transitions | 32.9–33.6 MiB |
| Private bytes after transitions | 9.5–9.9 MiB |
| Additional helper processes | 0 |
| Renders during sampled idle interval | 0 |
| GDI objects before / after transitions | 2 / 2 |
| Process CPU time during sampled idle interval | 0–15.6 ms |

Tray registration and the Ctrl+Alt+X shortcut succeeded on the test machine. Separate local interaction checks confirmed that toggling the notch preserves foreground focus and launching a second instance leaves only one Xylo process.

The regular app blocks waiting for Windows messages when idle. Active animation is paced through desktop composition; the loop returns to waiting when the springs settle. A one-shot grace timer handles hover exit, and the smoke test uses a temporary timer to sequence its checks.

These short tests do not establish long-running memory stability, behavior under notification bursts, or memory usage with agent adapters and clipboard history. Measurements may vary by runtime, display scaling, fonts, and OS. The target remains at most 50 MB idle including any future helper processes; it must be measured again as features are added. MiB values above use 1,048,576 bytes per unit.

After adding top-edge attachment, hover activation, composition-paced animation, and content fading, the local smoke test measured 34.2 MiB working set, 10.7 MiB private bytes, zero idle renders, and unchanged GDI object count (2). This remains a short desktop-shell measurement.

With the Windows media service and an active lyric lookup, a short real-session run measured approximately 74 MiB working set and 19 MiB private bytes. This exceeds the 50 MB working-set target; the smoke test disables live media and cosmetic animation and must not be used as a measurement of that full configuration. Further memory work and long-running tests are needed.
