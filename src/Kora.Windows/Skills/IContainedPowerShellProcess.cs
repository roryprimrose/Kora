namespace Kora.Windows.Skills;

/// <summary>One host-owned process tree with only three explicit unprivileged transport handles.</summary>
internal interface IContainedPowerShellProcess : IAsyncDisposable
{
    /// <summary>Starts only after the caller owns this object, retaining partial startup resources on failure.</summary>
    Task StartAsync(Func<bool> current, CancellationToken cancellationToken);
    /// <summary>Writes only to this worker's private standard input.</summary>
    Stream Input { get; }
    /// <summary>Reads only this worker's private standard output.</summary>
    Stream Output { get; }
    /// <summary>Reads only this worker's private standard error.</summary>
    Stream Error { get; }
    /// <summary>Stops the entire owned job to unblock transport and terminate descendants.</summary>
    void Stop();
    /// <summary>Joins the main process and all job descendants; failure is not quiescence.</summary>
    Task<int> JoinAsync();
}