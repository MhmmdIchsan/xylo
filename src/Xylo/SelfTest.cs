namespace Xylo;

internal static class SelfTest
{
    public static int RunWeather()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var result = WeatherService.FetchAsync(timeout.Token).GetAwaiter().GetResult();
            return result.Temperature is >= -100 and <= 80 ? 0 : 1;
        }
        catch { return 1; }
    }

    public static int Run()
    {
        var lines = LyricsService.Parse("[00:02.50][00:04.00]first\n[00:07,25]second\n[ar:artist]");
        var synced = new LyricsSnapshot("test", lines, null, "missing");
        var plain = new LyricsSnapshot("test", Array.Empty<LyricLine>(), "one\ntwo\nthree\nfour", "missing");
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
        bool staysLocked = !review.CanApprove;
        review.Next();
        bool valid = lines.Length == 3 && lines[0].At == 2.5 && lines[1].At == 4 && lines[2].At == 7.25
            && synced.Current(1) == "♪" && synced.Current(3) == "first" && synced.Current(8) == "second"
            && plain.Current(0) == "one" && plain.Visible(0).SequenceEqual(new[] { "one", "two", "three" })
            && history.Items.Count == 20 && history.Items[0] == "5" && history.Items.Count(x => x == "5") == 1
            && !timer.Running && timer.Remaining is >= 59 and <= 60 && rejectsInvalidDate
            && task is { Title: "new task", Done: true, HasTime: true, Due.Year: 2030, Due.Month: 12, Due.Day: 24, Due.Hour: 13, Due.Minute: 30 }
            && startsLocked && staysLocked && review.CanApprove && ApprovalReview.SafeDisplay("a\u202Eb\t") == "a\\u202Eb\\u0009";
        return valid ? 0 : 1;
    }
}
