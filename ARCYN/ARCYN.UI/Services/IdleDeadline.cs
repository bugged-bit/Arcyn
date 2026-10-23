namespace ARCYN.UI.Services;

internal sealed class IdleDeadline(Func<long>? milliseconds = null)
{
    private readonly Func<long> _milliseconds = milliseconds ?? (() => Environment.TickCount64);
    private long _reset;
    public long Elapsed => Math.Max(0, _milliseconds() - _reset);
    public void Reset() => _reset = _milliseconds();
    public bool Expired(int seconds) => seconds > 0 && Elapsed >= (long)seconds * 1000;
}
