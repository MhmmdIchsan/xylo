namespace Xylo;

internal static class SelfTest
{
    public static int RunCodexLimits()
    {
        using var service = new CodexLimitService(() => { });
        service.RefreshIfDue();
        for (int i = 0; i < 180 && service.Status == "Loading Codex limits..."; i++) Thread.Sleep(100);
        return service.Snapshot is { Primary: not null } or { Secondary: not null } ? 0 : 1;
    }

    public static int RunAgentInput()
    {
        try
        {
            using var bridge = new AgentBridge(() => { }, _ => { });
            bridge.Start();
            void Send(string kind, string request = "")
            {
                using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", "xylo-agent", System.IO.Pipes.PipeDirection.InOut);
                pipe.Connect(5000);
                string json = System.Text.Json.JsonSerializer.Serialize(new { agent = "Codex", @event = kind, session = "xylo-selftest", project = "test", requestId = request, windowHandle = 1234, windowPid = 55 });
                using var writer = new StreamWriter(pipe, new System.Text.UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(pipe, System.Text.Encoding.UTF8, leaveOpen: true);
                writer.WriteLine(json);
                if (reader.ReadLine() != "{}") throw new InvalidOperationException("Unexpected response");
            }
            Send("UserPromptSubmit");
            Send("InputRequired", "r1");
            bool waiting = bridge.Notice is { InputRequired: true } && bridge.FindActivity("Codex:xylo-selftest") is { Working: false, WindowHandle: 1234, WindowPid: 55 };
            Send("InputResolved", "r1");
            bool resumed = bridge.Notice == null && bridge.FindActivity("Codex:xylo-selftest") is { Working: true };
            Send("InputRequired", "r2");
            Send("Interrupt");
            bool interrupted = bridge.Notice == null && bridge.FindActivity("Codex:xylo-selftest") is { Working: false, Interrupted: true, WindowHandle: 1234, WindowPid: 55 };
            Send("SessionEnd");
            return waiting && resumed && interrupted && bridge.Activity == null ? 0 : 1;
        }
        catch { return 1; }
    }

    private static bool TestAgentAnimation()
    {
        var agent = new AgentActivity("Codex", "test", "test", true, DateTimeOffset.UtcNow);
        using var first = new SkiaSharp.SKBitmap(700, 420);
        using var second = new SkiaSharp.SKBitmap(700, 420);
        using var renderer = new IslandRenderer();
        bool HasMotion(AgentActivity? state, AgentApproval? approval, AgentNotice? notice)
        {
            void Render(SkiaSharp.SKBitmap bitmap, double seconds)
            {
                using var canvas = new SkiaSharp.SKCanvas(bitmap);
                renderer.Draw(canvas, 1, 400, 236, false, new BatteryStatus(null, false, null), seconds, -1,
                    null, null, false, new FocusTimer(), approval, 0, state, null, notice, Array.Empty<TaskItem>(), 1,
                    null, null, null, null, null, false, 0, Array.Empty<NotificationPreview>(), false, false, Array.Empty<string>(), true,
                    Enumerable.Range(0, 7).ToArray(), state == null ? Array.Empty<AgentActivity>() : new[] { state }, false, null, "Unavailable");
                canvas.Flush();
            }
            Render(first, 0); Render(second, 0.7);
            for (int y = 6; y <= 37; y++)
                for (int x = 158; x <= 196; x++)
                    if (first.GetPixel(x, y) != second.GetPixel(x, y)) return true;
            return false;
        }
        var approval = new AgentApproval("Codex", "test", "Bash", "test", "test", new TaskCompletionSource<string>());
        var input = new AgentNotice("Codex", "test", "r", "User input", "test", DateTimeOffset.UtcNow, true);
        var interrupted = agent with { Working = false, Interrupted = true };
        return HasMotion(agent, null, null) && HasMotion(null, approval, null) && HasMotion(null, null, input) && !HasMotion(null, null, null)
            && IslandApp.MascotNeedsAnimation(agent, null, null) && IslandApp.MascotNeedsAnimation(null, approval, null)
            && IslandApp.MascotNeedsAnimation(null, null, input) && !IslandApp.MascotNeedsAnimation(interrupted, null, null);
    }

    private static bool TestMediaRendering()
    {
        using var cover = new SkiaSharp.SKBitmap(4, 4);
        cover.Erase(new SkiaSharp.SKColor(220, 20, 20));
        using var image = SkiaSharp.SKImage.FromBitmap(cover);
        using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        var media = new MediaSnapshot("Test track", "Test artist", "Test album", true, 30, 120, DateTimeOffset.UtcNow, encoded.ToArray(), true);
        using var bitmap = new SkiaSharp.SKBitmap(700, 420);
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        using var renderer = new IslandRenderer();
        renderer.Draw(canvas, 1, 660, 236, false, new BatteryStatus(null, false, null), 0, -1,
            media, new LyricsSnapshot("test", Array.Empty<LyricLine>(), "one\ntwo", "missing"), true,
            new FocusTimer(), null, 0, null, null, null, Array.Empty<TaskItem>(), 4,
            null, null, null, null, null, false, 0, Array.Empty<NotificationPreview>(), false, false, Array.Empty<string>(), true, Enumerable.Range(0, 7).ToArray(), Array.Empty<AgentActivity>(), false, null, "Unavailable");
        canvas.Flush();
        var pixel = bitmap.GetPixel(89, 139);
        return pixel.Red > 180 && pixel.Green < 60;
    }

    public static int RunBrightness()
    {
        using var service = new BrightnessHudService(_ => { });
        service.Start();
        for (int i = 0; i < 50 && !service.Initialized; i++) Thread.Sleep(100);
        return service.Monitoring ? 0 : 1;
    }

    public static int RunVolume()
    {
        using var service = new VolumeHudService(_ => { });
        service.Start();
        for (int i = 0; i < 50 && !service.Initialized; i++) Thread.Sleep(100);
        return service.Monitoring ? 0 : 1;
    }

    public static int RunConnectivity()
    {
        using var service = new ConnectivityService(_ => { });
        service.Start();
        return service.NetworkMonitoring || service.BluetoothMonitoring ? 0 : 1;
    }

    public static int RunWeather()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var result = WeatherService.FetchAsync(timeout.Token).GetAwaiter().GetResult();
            return result.Temperature is >= -100 and <= 80 && result.Forecast.Length == 4 && result.Forecast.Zip(result.Forecast.Skip(1), (a, b) => a.Time < b.Time).All(x => x) ? 0 : 1;
        }
        catch { return 1; }
    }

    public static int Run()
    {
        var lines = LyricsService.Parse("[00:02.50][00:04.00]first\n[00:07,25]second\n[ar:artist]");
        var synced = new LyricsSnapshot("test", lines, null, "missing");
        var plain = new LyricsSnapshot("test", Array.Empty<LyricLine>(), "one\ntwo\nthree\nfour", "missing");
        var duplicateTimes = new LyricsSnapshot("test", new[] { new LyricLine(2, "first"), new LyricLine(2, "second"), new LyricLine(4, " ") }, null, "missing");
        var history = new ClipboardHistory();
        for (int i = 0; i < 22; i++) history.Add(i.ToString());
        history.Add("5");
        var timer = new FocusTimer();
        timer.StartPreset("Test", 60);
        timer.Toggle();
        var composer = new TaskComposer(0, new TaskItem { Title = "old", Done = true });
        composer.SelectAll();
        composer.Insert("new task");
        composer.SelectField(true);
        composer.Insert("not a date");
        bool rejectsInvalidDate = composer.Build() == null;
        composer.SelectAll();
        composer.Insert("2030-12-24 13:30");
        var task = composer.Build();
        var request = new AgentApproval("Codex", "s", "Bash", "project", "one\ntwo", new TaskCompletionSource<string>());
        var review = new ApprovalReview(request, _ => Enumerable.Range(0, 25).Select(i => i.ToString()).ToArray());
        bool startsLocked = !review.CanApprove;
        review.Next();
        var lowBattery = BatteryStatus.Alert(new BatteryStatus(21, false, false), new BatteryStatus(20, false, false));
        var plugged = BatteryStatus.Alert(new BatteryStatus(40, false, false), new BatteryStatus(40, true, true));
        var unknownPower = BatteryStatus.Alert(new BatteryStatus(40, false, null), new BatteryStatus(40, true, true));
        var wifiConnected = ConnectivityService.WifiAlert(new WifiState(false, ""), new WifiState(true, "test network"));
        var wifiSame = ConnectivityService.WifiAlert(new WifiState(true, "test network"), new WifiState(true, "test network"));
        var muted = VolumeHudService.Preview(0.8f, true);
        using var limitsJson = System.Text.Json.JsonDocument.Parse("{\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":25,\"windowDurationMins\":300,\"resetsAt\":1730947200},\"secondary\":{\"usedPercent\":70,\"windowDurationMins\":10080}}}}");
        var limits = CodexLimitService.Parse(limitsJson.RootElement);
        bool staysLocked = !review.CanApprove;
        review.Next();
        bool valid = TestMediaRendering() && TestAgentAnimation() && lines.Length == 3 && lines[0].At == 2.5 && lines[1].At == 4 && lines[2].At == 7.25
            && synced.Current(1) == "♪" && synced.Current(3) == "first" && synced.Current(8) == "second"
            && plain.Current(0) == "one" && plain.Visible(0).SequenceEqual(new[] { "one", "two", "three" })
            && ReferenceEquals(plain.Visible(0), plain.Visible(5)) && duplicateTimes.Current(2) == "second" && duplicateTimes.Current(4) == "♪" && duplicateTimes.Visible(2)[0] == "second"
            && history.Items.Count == 20 && history.Items[0] == "5" && history.Items.Count(x => x == "5") == 1
            && !timer.Running && timer.Remaining is >= 59 and <= 60 && rejectsInvalidDate
            && task is { Title: "new task", Done: true, HasTime: true, Due.Year: 2030, Due.Month: 12, Due.Day: 24, Due.Hour: 13, Due.Minute: 30 }
            && startsLocked && staysLocked && review.CanApprove && ApprovalReview.SafeDisplay("a\u202Eb\t") == "a\\u202Eb\\u0009"
            && lowBattery?.Title == "Battery low" && plugged?.Title == "Power connected" && unknownPower == null
            && wifiConnected?.Title == "Wi-Fi connected" && wifiSame == null && muted.Title == "Muted" && muted.Progress == 0
            && VolumeHudService.Preview(0.7f, false, 0.5f).Title == "Volume up" && VolumeHudService.Preview(0.4f, false, 0.5f).Title == "Volume down"
            && NotificationService.CreatePreview("WhatsApp", "Incoming call", "Alice").Kind == "whatsapp-call"
            && NotificationService.CreatePreview("WhatsApp", "Akhyar", "Hi").IsAppNotification && !plugged!.IsAppNotification
            && !IslandApp.ShouldShowPreview(true, NotificationService.CreatePreview("WhatsApp", "Akhyar", "Hi"))
            && IslandApp.ShouldShowPreview(true, plugged) && IslandApp.ShouldShowPreview(false, NotificationService.CreatePreview("WhatsApp", "Akhyar", "Hi"))
            && NotificationService.SourceLabel(NotificationService.CreatePreview("WhatsApp", "Akhyar", "Halo")) == "WhatsApp / Akhyar"
            && NotificationService.SourceLabel(NotificationService.CreatePreview("WhatsApp", "Group A", "Halo")) == "WhatsApp / Group A"
            && NotificationService.SourceLabel(NotificationService.CreatePreview("Chrome", "Group A", "Hello")) == "Chrome / Group A"
            && NotificationService.CreatePreview("Chrome", "News", "WhatsApp mentioned").Kind == "notification"
            && NotificationService.CreatePreview("Chrome", "web.whatsapp.com", "Hello").Kind == "whatsapp"
            && AgentBridge.ActivityForEvent("Codex", "s", "p", "UserPromptSubmit").Working
            && AgentBridge.ActivityForEvent("Codex", "s", "p", "Interrupt") is { Working: false, Interrupted: true }
            && AgentBridge.ActivityForEvent("Codex", "s", "p", "Stop") is { Working: false, Interrupted: false }
            && AgentBridge.ActivityForEvent("Codex", "s", "p", "Stop", AgentBridge.ActivityForEvent("Codex", "s", "p", "Interrupt")) is { Working: false, Interrupted: true }
            && limits?.Primary is { RemainingPercent: 75, Label: "5h" } && limits.Secondary is { RemainingPercent: 30, Label: "Weekly" }
            && BrightnessHudService.Preview(55).Progress == 0.55f;
        return valid ? 0 : 1;
    }
}
