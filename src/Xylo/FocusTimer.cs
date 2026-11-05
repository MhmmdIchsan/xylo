using System.Diagnostics;

namespace Xylo;

internal sealed class FocusTimer
{
    private long _endsAt;
    private int _remaining = 25 * 60;
    public string Label { get; private set; } = "Focus";
    public bool Running { get; private set; }
    public bool Finished { get; private set; }
    public bool Active { get; private set; }
    public int Remaining => Running ? Math.Max(0, (int)Math.Ceiling((_endsAt - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency)) : _remaining;
    public string Display => Finished ? $"{Label} complete" : $"{Remaining / 60:00}:{Remaining % 60:00}";

    public void StartPreset(string label, int seconds)
    {
        Label = label;
        Active = true;
        _remaining = seconds;
        Finished = false;
        Resume();
    }
    public void Toggle()
    {
        if (Running) { _remaining = Remaining; Running = false; }
        else if (Active && _remaining > 0) Resume();
    }
    private void Resume()
    {
        _endsAt = Stopwatch.GetTimestamp() + (long)(_remaining * (double)Stopwatch.Frequency);
        Running = true;
    }
    public bool Tick()
    {
        if (!Running || Remaining > 0) return false;
        _remaining = 0;
        Running = false;
        Finished = true;
        return true;
    }
    public void Clear() { Running = false; Finished = false; Active = false; _remaining = 25 * 60; Label = "Focus"; }
}
