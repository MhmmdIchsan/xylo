namespace Xylo;

internal enum MascotState { Awake, Sleeping, Working, Attention }
internal readonly record struct MascotBlend(float Sleep, float Work, float Attention)
{
    public static MascotBlend For(MascotState state) => state switch
    {
        MascotState.Sleeping => new(1, 0, 0), MascotState.Working => new(0, 1, 0),
        MascotState.Attention => new(0, 0, 1), _ => new(0, 0, 0)
    };
    public static MascotBlend Mix(MascotBlend from, MascotBlend to, float t) =>
        new(from.Sleep + (to.Sleep - from.Sleep) * t, from.Work + (to.Work - from.Work) * t, from.Attention + (to.Attention - from.Attention) * t);
}

internal sealed class MascotTransition
{
    public const double Duration = 0.5;
    private MascotState _target;
    private MascotBlend _from;
    private double _started;

    public MascotBlend Update(MascotState target, double now)
    {
        MascotBlend current = Current(now);
        if (target != _target) { _from = current; _target = target; _started = now; }
        return Current(now);
    }

    private MascotBlend Current(double now)
    {
        float t = (float)Math.Clamp((now - _started) / Duration, 0, 1);
        t = t * t * (3 - 2 * t);
        return MascotBlend.Mix(_from, MascotBlend.For(_target), t);
    }
}
