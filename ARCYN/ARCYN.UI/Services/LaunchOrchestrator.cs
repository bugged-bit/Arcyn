using System.Diagnostics;
using ARCYN.UI.Models;
namespace ARCYN.UI.Services;

public sealed class LaunchOrchestrator(LogService log)
{
    internal Func<ProcessStartInfo, Process?> Dispatch { get; set; } = Process.Start;
    internal Func<CancellationToken, Task> Cadence { get; set; } = ct => Task.Delay(350, ct);
    public async Task<LaunchResult> LaunchModeAsync(ModeConfig mode, CancellationToken token, IProgress<LaunchProgress>? progress = null)
    {
        var targets = mode.LaunchTargets.ToList();
        var result = new LaunchResult { TotalTargets = targets.Count };
        var completed = 0;
        try
        {
            token.ThrowIfCancellationRequested();
            if (targets.Count == 0)
            {
                result.Failures.Add("This workspace has no targets. Edit it to add an application, folder or URL.");
                return result;
            }
            foreach (var target in targets)
            {
                token.ThrowIfCancellationRequested();
                string? failure = null;
                try
                {
                    var dispatched = await Task.Run(() =>
                    {
                        token.ThrowIfCancellationRequested();
                        if (!LaunchService.TryPrepare(target, out var psi, out var error)) return error;
                        token.ThrowIfCancellationRequested();
                        using var process = Dispatch(psi);
                        return (string?)null;
                    }, token).WaitAsync(token);
                    failure = dispatched;
                    if (failure == null) result.LaunchedTargets++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { failure = ex.Message; }
                if (failure != null) { result.Failures.Add(target.DisplayLabel + ": " + failure); log.Write("Launch failed: {0}", failure); }
                progress?.Report(new LaunchProgress { CompletedTargets = ++completed, TotalTargets = targets.Count, CurrentLabel = target.DisplayLabel, Error = failure });
                await Cadence(token);
            }
        }
        catch (OperationCanceledException) { result.Canceled = true; }
        return result;
    }
}
public sealed class LaunchResult
{
    public int LaunchedTargets { get; set; }
    public int TotalTargets { get; set; }
    public List<string> Failures { get; } = [];
    public bool Canceled { get; set; }
    public bool Success => !Canceled && Failures.Count == 0 && LaunchedTargets > 0;
}
public sealed class LaunchProgress
{
    public int CompletedTargets { get; set; }
    public int TotalTargets { get; set; }
    public string? CurrentLabel { get; set; }
    public string? Error { get; set; }
}
