namespace Xylo;

internal static class SelfTest
{
    public static int Run()
    {
        var lines = LyricsService.Parse("[00:02.50][00:04.00]first\n[00:07,25]second\n[ar:artist]");
        var synced = new LyricsSnapshot("test", lines, null, "missing");
        var plain = new LyricsSnapshot("test", Array.Empty<LyricLine>(), "one\ntwo\nthree\nfour", "missing");
        bool valid = lines.Length == 3 && lines[0].At == 2.5 && lines[1].At == 4 && lines[2].At == 7.25
            && synced.Current(1) == "♪" && synced.Current(3) == "first" && synced.Current(8) == "second"
            && plain.Current(0) == "one" && plain.Visible(0).SequenceEqual(new[] { "one", "two", "three" });
        return valid ? 0 : 1;
    }
}
