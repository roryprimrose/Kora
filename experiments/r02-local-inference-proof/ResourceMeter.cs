using System.Diagnostics;

namespace R02Proof;

internal sealed record ResourceResult(
    int Samples, long PeakAggregateWorkingSetBytes, long PeakAggregatePrivateBytes,
    double SampledCpuSeconds, double AverageMachineCpuPercent, int[] ProcessIds,
    string[] ObservationErrors);

internal sealed class ResourceMeter : IAsyncDisposable
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource stop = new();
    private readonly Dictionary<int, double> previousCpu = [];
    private readonly HashSet<int> processIds = [];
    private readonly HashSet<string> errors = [];
    private readonly Task sampling;
    private readonly int sessionId;
    private int samples;
    private long peakWorkingSet;
    private long peakPrivate;
    private double cpuSeconds;

    public ResourceMeter()
    {
        using var self = Process.GetCurrentProcess();
        sessionId = self.SessionId;
        Sample(baseline: true);
        sampling = SampleAsync();
    }

    public async Task<ResourceResult> FinishAsync()
    {
        await stop.CancelAsync();
        await sampling;
        Sample(baseline: false);
        clock.Stop();
        return new(samples, peakWorkingSet, peakPrivate, cpuSeconds,
            cpuSeconds / Math.Max(clock.Elapsed.TotalSeconds, 0.001) / Environment.ProcessorCount * 100,
            processIds.Order().ToArray(), errors.Order().ToArray());
    }

    private async Task SampleAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(stop.Token))
                Sample(baseline: false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    private void Sample(bool baseline)
    {
        long workingSet = 0;
        long privateBytes = 0;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != sessionId
                        || !process.ProcessName.StartsWith("ollama", StringComparison.OrdinalIgnoreCase))
                        continue;
                    processIds.Add(process.Id);
                    workingSet += process.WorkingSet64;
                    privateBytes += process.PrivateMemorySize64;
                    var cpu = process.TotalProcessorTime.TotalSeconds;
                    if (previousCpu.TryGetValue(process.Id, out var previous))
                        cpuSeconds += Math.Max(0, cpu - previous);
                    else if (!baseline)
                        cpuSeconds += cpu;
                    previousCpu[process.Id] = cpu;
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    errors.Add(exception.GetType().Name + ": process exited or could not be sampled.");
                }
            }
        }
        samples++;
        peakWorkingSet = Math.Max(peakWorkingSet, workingSet);
        peakPrivate = Math.Max(peakPrivate, privateBytes);
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        await sampling;
        stop.Dispose();
    }
}
