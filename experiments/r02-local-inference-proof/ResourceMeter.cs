using System.Diagnostics;

namespace R02Proof;

internal sealed record ResourceResult(
    int Samples, long PeakAggregateWorkingSetBytes, long PeakAggregatePrivateBytes,
    double SampledCpuSeconds, double AverageMachineCpuPercent, int[] ProcessIds,
    string[] ObservationErrors, string Coverage, string ObservationStatus,
    ProcessIdentity? Root, ProcessResourceSample[] ObservedProcesses,
    ProcessRootState[] ObservedRootStates, int FailedSnapshots);

internal sealed class ResourceAccumulator
{
    private readonly Dictionary<ProcessIdentity, double> previousCpu = [];
    private readonly Dictionary<ProcessIdentity, ProcessResourceSample> observed = [];
    private readonly HashSet<string> errors = [];
    private readonly HashSet<ProcessRootState> rootStates = [];
    public int Samples { get; private set; }
    public long PeakWorkingSet { get; private set; }
    public long PeakPrivate { get; private set; }
    public double CpuSeconds { get; private set; }
    public int FailedSnapshots { get; private set; }
    public ProcessRootState[] RootStates => rootStates.Order().ToArray();
    public string[] Errors => errors.Order().ToArray();
    public ProcessResourceSample[] Processes => observed.Values.OrderBy(p => p.Identity.Id)
        .ThenBy(p => p.Identity.StartedUtcTicks).ToArray();

    public void Error(string error)
    {
        FailedSnapshots++;
        errors.Add(error);
    }

    public void Add(ProcessSelection selection)
    {
        foreach (var error in selection.Errors) errors.Add(error);
        rootStates.Add(selection.RootState);
        long workingSet = 0;
        long privateBytes = 0;
        foreach (var process in selection.Processes)
        {
            if (process.WorkingSetBytes < 0 || process.PrivateBytes < 0
                || !double.IsFinite(process.CpuSeconds) || process.CpuSeconds < 0)
                throw new InvalidDataException("Invalid process resource counters; observation cannot be accepted.");
            workingSet = checked(workingSet + process.WorkingSetBytes);
            privateBytes = checked(privateBytes + process.PrivateBytes);
            observed[process.Identity] = process;
            if (previousCpu.TryGetValue(process.Identity, out var previous))
            {
                if (process.CpuSeconds < previous)
                    errors.Add($"CPU counter decreased for process {process.Identity.Id}; observation continuity lost.");
                else CpuSeconds += process.CpuSeconds - previous;
            }
            // The first counter is a baseline, never an existing process's lifetime CPU charge.
            previousCpu[process.Identity] = process.CpuSeconds;
        }
        Samples++;
        PeakWorkingSet = Math.Max(PeakWorkingSet, workingSet);
        PeakPrivate = Math.Max(PeakPrivate, privateBytes);
    }
}

internal sealed class ResourceMeter : IAsyncDisposable
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource stop = new();
    private readonly ResourceAccumulator accumulator = new();
    private readonly Task sampling;
    private readonly ProcessTreeObserver? observer;
    private ResourceResult? result;

    public ResourceMeter(ProcessTreeObserver? observer = null)
    {
        this.observer = observer;
        if (observer is not null) Sample();
        sampling = observer is null ? Task.CompletedTask : SampleAsync();
    }

    public async Task<ResourceResult> FinishAsync()
    {
        if (result is not null) return result;
        await stop.CancelAsync();
        await sampling;
        if (observer is not null) Sample();
        clock.Stop();
        result = new(accumulator.Samples, accumulator.PeakWorkingSet, accumulator.PeakPrivate, accumulator.CpuSeconds,
            accumulator.CpuSeconds / Math.Max(clock.Elapsed.TotalSeconds, 0.001) / Environment.ProcessorCount * 100,
            accumulator.Processes.Select(p => p.Identity.Id).Distinct().Order().ToArray(), accumulator.Errors,
            observer is null
                ? "Not observed: no admitted native root. Zero counters are not model resource measurements."
                : "Partial: admitted PID/creation-time server tree and observed descendants regardless of names. Polling misses short-lived/breakaway processes and parent exits before admission; first CPU counters are baselines. Shared pages may be double-counted. Not per-request attribution, exclusivity, server cessation or egress proof.",
            observer is null ? "Not observed" : accumulator.Errors.Length > 0 ? "Incomplete" : "Sampled; completeness unproved",
            observer?.Root, accumulator.Processes, accumulator.RootStates, accumulator.FailedSnapshots);
        return result;
    }

    private async Task SampleAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(stop.Token))
                Sample();
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    private void Sample()
    {
        try
        {
            accumulator.Add(observer!.Capture());
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
            or IOException or InvalidOperationException or ArgumentException or OverflowException)
        {
            accumulator.Error(exception is System.ComponentModel.Win32Exception native
                ? $"Native resource snapshot failed: Win32 error {native.NativeErrorCode}."
                : $"Native resource snapshot failed: {exception.GetType().Name}.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        await sampling;
        stop.Dispose();
    }
}
