using System.Diagnostics;
using System.IO;
using ARCYN.UI.Models;

namespace ARCYN.UI.Services;

public sealed record CommandReview(string Executable, IReadOnlyList<string> Arguments, string? WorkingDirectory, bool Elevate);
public sealed record StepOutcome(string Id, string Label, string Evidence, string? Error = null);

public sealed class LaunchOrchestrator
{
    private readonly LogService _log;
    private readonly IWindowSystem _windows;
    private readonly Dictionary<string, Dictionary<string, WindowSnapshot>> _managed = [];
    private readonly Dictionary<nint, WindowSnapshot> _minimized = [];
    private readonly HashSet<string> _uncertain = [];
    private int _running;
    internal Func<ProcessStartInfo, Process?> Dispatch { get; set; } = Process.Start;
    internal Func<CancellationToken, Task> Cadence { get; set; } = ct => Task.Delay(350, ct);
    internal Func<int, CancellationToken, Task> Delay { get; set; } = (ms, ct) => Task.Delay(ms, ct);
    internal Func<long> Milliseconds { get; set; } = () => Environment.TickCount64;
    public Func<IReadOnlyList<CommandReview>, CancellationToken, Task<bool>> AuthorizeCommands { get; set; } = (_, _) => Task.FromResult(false);
    internal Func<ProcessStartInfo, CancellationToken, Task<int>>? CommandExit { get; set; }
    public LaunchOrchestrator(LogService log, IWindowSystem? windows = null) { _log = log; _windows = windows ?? new WindowsWindowSystem(); }

    public async Task<LaunchResult> LaunchModeAsync(ModeConfig mode, CancellationToken token, IProgress<LaunchProgress>? progress = null)
    {
        var result = new LaunchResult();
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) { result.Failures.Add("A workspace operation is already running."); return result; }
        try
        {
            token.ThrowIfCancellationRequested();
            mode = WorkspaceDefinition.Clone(mode);
            WorkspaceDefinition.Validate(mode);
            var targets = mode.LaunchTargets.ToList();
            var needsWindow = mode.Layout.Select(l => l.TargetId).Concat(mode.Actions.OfType<WindowActionConfig>().Select(a => a.TargetId)).ToHashSet(StringComparer.Ordinal);
            foreach (var target in targets.Where(t => needsWindow.Contains(t.Id))) target.Readiness = Readiness.Window;
            var steps = targets.Select(t => (t.Id, Label: t.DisplayLabel, Dependencies: t.DependsOn, Target: (TargetConfig?)t, Action: (WorkspaceActionConfig?)null))
                .Concat(mode.Actions.Select(a => (a.Id, Label: string.IsNullOrWhiteSpace(a.Label) ? a.GetType().Name.Replace("ActionConfig", "") : a.Label, Dependencies: WorkspaceDefinition.Dependencies(a), Target: (TargetConfig?)null, Action: (WorkspaceActionConfig?)a))).ToList();
            result.TotalTargets = steps.Count;
            if (steps.Count == 0) { result.Failures.Add("This workspace has no targets or actions. Edit it to add one."); return result; }
            var commandInfo = new Dictionary<string, ProcessStartInfo>();
            var reviews = new List<CommandReview>();
            await Task.Run(() => { foreach (var step in steps)
            {
                token.ThrowIfCancellationRequested();
                if (step.Action is CommandActionConfig command)
                {
                    var info = PrepareCommand(command); commandInfo.Add(step.Id, info);
                    reviews.Add(new(info.FileName, info.ArgumentList.ToArray(), info.WorkingDirectory, command.Elevate));
                }
                else if (step.Target is { } target && RequiresReview(target))
                {
                    if (!LaunchService.TryPrepare(target, out var info, out var error)) throw new InvalidDataException(error);
                    reviews.Add(new(info.FileName, info.ArgumentList.ToArray(), info.WorkingDirectory, false));
                }
            } }, token).WaitAsync(token);
            if (reviews.Count > 0 && !await AuthorizeCommands(reviews, token)) { result.Canceled = true; return result; }
            token.ThrowIfCancellationRequested();
            if (!_managed.TryGetValue(mode.Id, out var bindings)) _managed[mode.Id] = bindings = [];
            var assigned = new HashSet<nint>();
            var completed = new Dictionary<string, bool>(StringComparer.Ordinal);
            var remaining = steps.ToList();
            WindowSnapshot? finalFocus = null;
            while (remaining.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var step = remaining.First(s => s.Dependencies.All(completed.ContainsKey)); remaining.Remove(step);
                string? failure = null; var evidence = "Completed";
                void Stage(string stage) => progress?.Report(new() { CompletedTargets = completed.Count, TotalTargets = steps.Count, CurrentLabel = step.Label, Evidence = stage });
                try
                {
                    if (step.Dependencies.Any(id => !completed[id])) { failure = "A prerequisite failed; step skipped."; evidence = "Skipped"; }
                    else if (step.Target is { } target)
                    {
                        var resolved = await RunTarget(mode.Id, target, assigned, token, Stage);
                        evidence = resolved.Evidence;
                        if (resolved.Window is { } window)
                        {
                            bindings[target.Id] = window; assigned.Add(window.Handle);
                            if (mode.Layout.FirstOrDefault(l => l.TargetId == target.Id) is { } layout)
                            {
                                Stage("Arranging");
                                await ApplyLayout(window, layout, result, token);
                                if (layout.Focus) finalFocus = window;
                            }
                        }
                        result.LaunchedTargets++;
                    }
                    else if (step.Action is CommandActionConfig command)
                    {
                        Stage("Running reviewed command");
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                        deadline.CancelAfter(command.TimeoutMs);
                        try
                        {
                            var info = commandInfo[command.Id];
                            var exit = await (CommandExit?.Invoke(info, deadline.Token) ?? RunCommand(info, deadline.Token)).WaitAsync(deadline.Token);
                            if (exit != 0) throw new InvalidOperationException("Command exited with code " + exit + ".");
                            evidence = "Exit 0";
                        }
                        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Command timed out; dispatched work was not terminated."); }
                    }
                    else if (step.Action is WaitActionConfig wait)
                    {
                        Stage("Waiting");
                        if (wait.TargetId == null) await Delay(wait.DelayMs, token);
                        else
                        {
                            var waitTarget = targets.Single(t => t.Id == wait.TargetId);
                            var identity = WindowMatching.Identity(waitTarget, _windows) ?? throw new InvalidOperationException("Window wait requires executable or application identity.");
                            var excluded = bindings.Where(p => p.Key != waitTarget.Id).Select(p => p.Value.Handle).ToHashSet();
                            var window = await WaitWindow(identity, excluded, wait.TimeoutMs, token);
                            bindings[waitTarget.Id] = window; evidence = "Window available";
                        }
                    }
                    else if (step.Action is WindowActionConfig action)
                    {
                        Stage("Managing window");
                        var window = Bound(bindings, action.TargetId);
                        if (action.Operation == WindowOperation.Focus) finalFocus = window;
                        else if (action.Operation == WindowOperation.ApplyLayout)
                            await ApplyLayout(window, mode.Layout.SingleOrDefault(l => l.TargetId == action.TargetId) ?? throw new InvalidOperationException("No layout is saved for this target."), result, token);
                        else if (action.Operation == WindowOperation.Minimize && IsShared(mode.Id, window)) result.Warnings.Add("Shared window left visible: " + window.Title);
                        else await OperateAsync(window, action.Operation, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    result.Outcomes.Add(new(step.Id, step.Label, "Canceled", "Already-dispatched work was left running."));
                    throw;
                }
                catch (Exception ex) { failure = ex.Message; if (evidence != "Skipped") evidence = ex is TimeoutException ? "Timed out" : "Failed"; }
                completed[step.Id] = failure == null;
                if (failure != null) { result.Failures.Add(step.Label + ": " + failure); _log.Write("Workspace step: {0}", failure); }
                else result.CompletedSteps++;
                result.Outcomes.Add(new(step.Id, step.Label, evidence, failure));
                progress?.Report(new() { CompletedTargets = completed.Count, TotalTargets = steps.Count, CurrentLabel = step.Label, Evidence = evidence, Error = failure, StepCompleted = true });
                await Cadence(token);
            }
            token.ThrowIfCancellationRequested();
            if (finalFocus != null)
            {
                var error = _windows.Focus(Current(finalFocus));
                if (error != null) result.Warnings.Add(error);
            }
        }
        catch (OperationCanceledException) { result.Canceled = true; }
        catch (Exception ex) { result.Failures.Add(ex.Message); }
        finally { Volatile.Write(ref _running, 0); }
        return result;
    }

    private async Task<(WindowSnapshot? Window, string Evidence)> RunTarget(string workspaceId, TargetConfig target, HashSet<nint> assigned, CancellationToken token, Action<string> stage)
    {
        stage("Finding application");
        var started = Milliseconds();
        var kind = WindowMatching.Kind(target);
        var identity = WindowMatching.Identity(target, _windows);
        var needsWindow = target.Readiness == Readiness.Window || target.Readiness == Readiness.Automatic && (kind == TargetKind.Application || target.Match != null);
        if (!needsWindow && target.InstancePolicy != InstancePolicy.ReuseOrLaunch) throw new InvalidOperationException("Instance policies require window readiness.");
        if (needsWindow && identity == null) throw new InvalidOperationException("Cannot resolve application identity. Select an executable or configure window matching; use Dispatch only for targets without a managed window.");
        IReadOnlyList<WindowSnapshot> before = needsWindow ? await Task.Run(_windows.Windows, token).WaitAsync(token) : [];
        var key = workspaceId + "/" + target.Id;
        if (needsWindow && target.InstancePolicy != InstancePolicy.LaunchNew)
        {
            var selection = WindowMatching.Select(identity!, before, assigned);
            if (selection.Error != null) throw new InvalidOperationException(selection.Error);
            if (selection.Window is { } existing && (kind == TargetKind.Application || target.InstancePolicy == InstancePolicy.ReuseOnly))
            {
                stage("Reusing · waiting for window");
                var ready = await WaitWindow(identity!, assigned, target.TimeoutMs, token, existing.Handle);
                _uncertain.Remove(key); return (ready, "Reused · window available");
            }
            if (target.InstancePolicy == InstancePolicy.ReuseOnly) throw new InvalidOperationException("No matching running window. Reuse-only target was not launched.");
        }
        if (_uncertain.Contains(key) && target.InstancePolicy != InstancePolicy.LaunchNew) throw new InvalidOperationException("Earlier dispatch outcome is unknown. Reattach its window or explicitly select Launch new before retrying.");
        if (!LaunchService.TryPrepare(target, out var info, out var error)) throw new InvalidOperationException(error);
        token.ThrowIfCancellationRequested();
        stage("Dispatching");
        _uncertain.Add(key);
        try { await Task.Run(() => { token.ThrowIfCancellationRequested(); using var process = Dispatch(info); }, token).WaitAsync(TimeSpan.FromMilliseconds(target.TimeoutMs), token); }
        catch (Exception ex) when (ex is not OperationCanceledException and not TimeoutException) { _uncertain.Remove(key); throw; }
        token.ThrowIfCancellationRequested();
        if (!needsWindow) { _uncertain.Remove(key); return (null, "Dispatched"); }
        // A broker may hand off to another process; only a distinct matching window establishes a new launch.
        var excluded = before.Select(w => w.Handle).Concat(assigned).ToHashSet();
        stage("Waiting for window");
        var window = await WaitWindow(identity!, excluded, Math.Max(1, target.TimeoutMs - (int)Math.Min(int.MaxValue, Milliseconds() - started)), token);
        _uncertain.Remove(key); return (window, "Window available");
    }

    private async Task<WindowSnapshot> WaitWindow(WindowMatch identity, ISet<nint>? exclude, int timeout, CancellationToken token, nint? required = null)
    {
        var start = Milliseconds();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var snapshot = await Task.Run(_windows.Windows, token).WaitAsync(token);
            var selection = WindowMatching.Select(identity, snapshot, exclude);
            if (selection.Error != null) throw new InvalidOperationException(selection.Error);
            if (selection.Window is { } w && (required == null || w.Handle == required) && await Task.Run(() => _windows.IsResponsive(w), token).WaitAsync(token)) return w;
            if (Milliseconds() - start >= timeout) throw new TimeoutException("No unique responsive window appeared; dispatch alone does not establish readiness.");
            await Delay(200, token);
        }
    }

    private async Task ApplyLayout(WindowSnapshot window, WindowLayout layout, LaunchResult result, CancellationToken token)
    {
        var placement = WorkspaceLayout.Restore(layout, _windows.Monitors());
        if (placement.Fallback) result.Warnings.Add("Saved monitor is disconnected; used primary monitor for " + window.Title + ".");
        token.ThrowIfCancellationRequested();
        var error = _windows.Place(Current(window), placement.Bounds, layout.State);
        if (error != null) throw new InvalidOperationException(error);
        var start = Milliseconds();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var current = Current(window); var a = current.NormalBounds; var b = placement.Bounds;
            if (current.State == layout.State && Math.Abs(a.X - b.X) <= 2 && Math.Abs(a.Y - b.Y) <= 2 && Math.Abs(a.Width - b.Width) <= 2 && Math.Abs(a.Height - b.Height) <= 2) return;
            if (Milliseconds() - start >= 1000) throw new InvalidOperationException("Application refused or adjusted requested placement.");
            await Delay(100, token);
        }
    }

    private WindowSnapshot Current(WindowSnapshot window)
    {
        var current = _windows.Windows().SingleOrDefault(w => w.Handle == window.Handle && w.ProcessId == window.ProcessId && w.ProcessStarted == window.ProcessStarted && w.ClassName == window.ClassName && string.Equals(w.Executable, window.Executable, StringComparison.OrdinalIgnoreCase));
        if (current == null || !_windows.IsCurrent(current)) throw new InvalidOperationException("Managed window disappeared or changed identity; reattach it.");
        return current;
    }
    private WindowSnapshot Bound(Dictionary<string, WindowSnapshot> bindings, string targetId) => bindings.TryGetValue(targetId, out var w) ? Current(w) : throw new InvalidOperationException("Target has no attached window. Attach running windows first.");
    private async Task OperateAsync(WindowSnapshot window, WindowOperation operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string? error;
        WindowShowState desired;
        if (operation == WindowOperation.Minimize)
        {
            if (window.State == WindowShowState.Minimized) return;
            desired = WindowShowState.Minimized;
            error = _windows.Show(window, WindowShowState.Minimized);
            if (error == null) _minimized.TryAdd(window.Handle, window);
        }
        else
        {
            if (!_minimized.TryGetValue(window.Handle, out var previous)) return;
            if (previous.ProcessId != window.ProcessId || previous.ProcessStarted != window.ProcessStarted) { _minimized.Remove(window.Handle); throw new InvalidOperationException("Minimized window identity changed."); }
            desired = previous.State;
            error = _windows.Show(window, desired);
        }
        if (error != null) throw new InvalidOperationException(error);
        var started = Milliseconds();
        while (Current(window).State != desired)
        {
            token.ThrowIfCancellationRequested();
            if (Milliseconds() - started >= 1000) throw new InvalidOperationException("Application did not confirm requested window state.");
            await Delay(100, token);
        }
        if (operation == WindowOperation.Restore) _minimized.Remove(window.Handle);
    }

    public async Task<LaunchResult> ManageAsync(ModeConfig mode, WindowOperation? operation, CancellationToken token)
    {
        var result = new LaunchResult();
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) { result.Failures.Add("A workspace operation is already running."); return result; }
        try
        {
            token.ThrowIfCancellationRequested(); mode = WorkspaceDefinition.Clone(mode); WorkspaceDefinition.Validate(mode);
            if (operation == null)
            {
                var bindings = new Dictionary<string, WindowSnapshot>(); var assigned = new HashSet<nint>();
                var snapshot = await Task.Run(_windows.Windows, token).WaitAsync(token);
                foreach (var target in mode.LaunchTargets)
                {
                    token.ThrowIfCancellationRequested();
                    var identity = WindowMatching.Identity(target, _windows); if (identity == null) continue;
                    var selected = WindowMatching.Select(identity, snapshot, assigned);
                    if (selected.Error != null) result.Failures.Add(target.DisplayLabel + ": " + selected.Error);
                    else if (selected.Window is { } w && _windows.IsCurrent(w)) { bindings[target.Id] = w; assigned.Add(w.Handle); }
                }
                _managed[mode.Id] = bindings; result.TotalTargets = bindings.Count; result.CompletedSteps = bindings.Count;
                if (bindings.Count == 0) result.Failures.Add("No windows attached. Configure matching or launch the workspace.");
                return result;
            }
            if (!_managed.TryGetValue(mode.Id, out var owned) || owned.Count == 0) { result.Failures.Add("Attach running windows first. Ownership is limited to this ARCYN session."); return result; }
            result.TotalTargets = owned.Count;
            WindowSnapshot? focus = null;
            foreach (var pair in owned.ToArray())
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var w = Bound(owned, pair.Key);
                    if (operation == WindowOperation.ApplyLayout)
                    {
                        var layout = mode.Layout.SingleOrDefault(l => l.TargetId == pair.Key);
                        if (layout == null) continue;
                        await ApplyLayout(w, layout, result, token); if (layout.Focus) focus = w;
                    }
                    else if (operation == WindowOperation.Focus) focus ??= w;
                    else if (operation == WindowOperation.Minimize && IsShared(mode.Id, w))
                        result.Warnings.Add("Shared window left visible: " + w.Title);
                    else await OperateAsync(w, operation.Value, token);
                    result.CompletedSteps++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { result.Failures.Add(ex.Message); }
            }
            token.ThrowIfCancellationRequested();
            if (focus != null && _windows.Focus(Current(focus)) is { } focusError) result.Warnings.Add(focusError);
        }
        catch (OperationCanceledException) { result.Canceled = true; }
        catch (Exception ex) { result.Failures.Add(ex.Message); }
        finally { Volatile.Write(ref _running, 0); }
        return result;
    }
    public void Release(string workspaceId)
    {
        if (Volatile.Read(ref _running) != 0) return;
        _managed.Remove(workspaceId);
        foreach (var handle in _minimized.Keys.Where(h => !_managed.Values.Any(b => b.Values.Any(w => w.Handle == h))).ToArray()) _minimized.Remove(handle);
    }
    private bool IsShared(string workspaceId, WindowSnapshot window) => _managed.Any(p => p.Key != workspaceId && p.Value.Values.Any(other => other.Handle == window.Handle && other.ProcessId == window.ProcessId && other.ProcessStarted == window.ProcessStarted));

    private bool RequiresReview(TargetConfig target)
    {
        var (cmd, _) = LaunchService.ParseTarget(target);
        cmd = Environment.ExpandEnvironmentVariables(cmd);
        if (Uri.TryCreate(cmd, UriKind.Absolute, out var uri) && !uri.IsFile) return false;
        var shortcut = cmd.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
        var resolved = _windows.ResolveExecutable(cmd);
        if (shortcut && resolved == null) throw new InvalidOperationException("Shortcut identity cannot be inspected. Select its executable or configure an explicit command action.");
        var extension = Path.GetExtension(cmd).ToLowerInvariant();
        var name = Path.GetFileNameWithoutExtension(resolved ?? cmd).ToLowerInvariant();
        var review = extension is ".bat" or ".cmd" or ".ps1" or ".psm1" or ".vbs" or ".vbe" or ".js" or ".jse" or ".wsf" or ".wsh" or ".hta" or ".reg" or ".msi" or ".py" or ".pyw" or ".rb" or ".pl" or ".php" or ".sh" ||
            name is "cmd" or "powershell" or "pwsh" or "wscript" or "cscript" or "mshta" or "reg" or "regsvr32" or "rundll32" or "diskpart" or "bcdedit" or "schtasks" or "sc" or "shutdown" or "format" or "python" or "python3" or "py" or "node" or "nodejs" or "ruby" or "perl" or "php" or "bash" or "sh" or "wsl" or "dotnet";
        if (shortcut && review) throw new InvalidOperationException("Shortcut targets a command host. Configure executable and arguments as an explicit command action for review.");
        return review;
    }
    private ProcessStartInfo PrepareCommand(CommandActionConfig command)
    {
        var exe = _windows.ResolveExecutable(command.Cmd) ?? throw new InvalidOperationException("Command requires an executable; select its interpreter explicitly for scripts.");
        if (!LaunchService.TryPrepare(new() { Cmd = exe, Args = command.Args, WorkingDirectory = command.WorkingDirectory }, out var info, out var error)) throw new InvalidOperationException(error);
        info.UseShellExecute = command.Elevate; info.CreateNoWindow = !command.Elevate;
        if (command.Elevate) info.Verb = "runas";
        return info;
    }
    private Task<int> RunCommand(ProcessStartInfo info, CancellationToken token) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested(); using var process = Dispatch(info);
        if (process == null) throw new InvalidOperationException("Command returned no process handle; completion cannot be established.");
        await process.WaitForExitAsync(token); return process.ExitCode;
    }, token);
}

public sealed class LaunchResult
{
    public int LaunchedTargets { get; set; }
    public int CompletedSteps { get; set; }
    public int TotalTargets { get; set; }
    public List<string> Failures { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<StepOutcome> Outcomes { get; } = [];
    public bool Canceled { get; set; }
    public bool Success => !Canceled && Failures.Count == 0 && Warnings.Count == 0 && CompletedSteps > 0;
}
public sealed class LaunchProgress
{
    public bool StepCompleted { get; set; }
    public int CompletedTargets { get; set; }
    public int TotalTargets { get; set; }
    public string? CurrentLabel { get; set; }
    public string? Evidence { get; set; }
    public string? Error { get; set; }
}
