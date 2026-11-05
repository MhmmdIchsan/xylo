namespace Xylo;

internal static class SelfTest
{
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
        bool staysLocked = !review.CanApprove;
        review.Next();
        bool valid = lines.Length == 3 && lines[0].At == 2.5 && lines[1].At == 4 && lines[2].At == 7.25
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
            && NotificationService.CreatePreview("Chrome", "News", "WhatsApp mentioned").Kind == "notification"
            && NotificationService.CreatePreview("Chrome", "web.whatsapp.com", "Hello").Kind == "whatsapp"
            && BrightnessHudService.Preview(55).Progress == 0.55f;
        return valid ? 0 : 1;
    }
}
