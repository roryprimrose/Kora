using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Kora.Core.Hosting;
using Kora.Core.Skills;

namespace Kora.Windows.Skills;

public sealed class WindowsBundledScriptWorker : IBundledScriptWorker
{
    private readonly Func<BundledWorkerProfile?> qualification;
    private readonly Func<BundledWorkerProfile, string, IContainedPowerShellProcess> launch;
    private int running;
    private IContainedPowerShellProcess? ownedProcess;

    // No installed contained runtime/profile has been qualified. There is deliberately no user/model enable switch.
    public WindowsBundledScriptWorker() : this(static () => null, ContainedPowerShellProcess.Create) { }

    internal WindowsBundledScriptWorker(Func<BundledWorkerProfile?> qualification,
        Func<BundledWorkerProfile, string, IContainedPowerShellProcess> launch)
    { this.qualification = qualification; this.launch = launch; }

    public string? QualifiedImplementationDigest => qualification()?.ImplementationDigest;
    public bool IsQuiescent => Volatile.Read(ref running) == 0 && Volatile.Read(ref ownedProcess) is null;

    public async Task<BundledScriptReceipt> RunAsync(BundledFixedScript script, HostRequest invocation,
        Func<bool> current, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(current);
        var profile = qualification();
        if (profile is null || invocation.InvocationId is null
            || invocation.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || cancellationToken.IsCancellationRequested || !current())
        { throw new InvalidOperationException("The exact bundled invocation or contained runtime is unavailable; no process was launched."); }
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
        { throw new InvalidOperationException("This host already owns a bundled worker that has not joined."); }
        var release = true;
        try
        {
            var nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            var id = invocation.InvocationId.Value.Value.ToString("D");
            var action = script.Action switch
            {
                FixedSessionAction.Lock => "lock",
                FixedSessionAction.Shutdown => "shutdown",
                FixedSessionAction.Restart => "restart",
                _ => throw new InvalidDataException("Unknown fixed action."),
            };
            var helpers = script.Package.Manifest.HelperLoadOrder.Select(name =>
                script.Package.Files.Single(file => string.Equals(file.Name, name, StringComparison.Ordinal))).ToArray();
            var entry = script.Package.Files.Single(file =>
                string.Equals(file.Name, script.Package.Manifest.EntryPoint, StringComparison.Ordinal));
            var input = JsonSerializer.Serialize(new
            {
                Nonce = nonce,
                Invocation = id,
                Action = action,
                Helpers = helpers.Select(file => new { file.Name, file.ResourceId, file.Text }),
                Entry = new { entry.Name, entry.ResourceId, entry.Text },
            });
            if (qualification() != profile || !current() || cancellationToken.IsCancellationRequested)
            { throw new InvalidOperationException("The exact source or runtime qualification changed before launch."); }
            var process = launch(profile, BundledPowerShellBootstrap.Source);
            Volatile.Write(ref ownedProcess, process);
            release = false;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                Exception? stopFailure = null;
                using var retirement = lifetime.Token.Register(() =>
                {
                    try { process.Stop(); }
                    catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
                    { Interlocked.CompareExchange(ref stopFailure, exception, null); }
                });
                bool LaunchCurrent() => qualification() == profile && current() && !lifetime.IsCancellationRequested;
                if (!LaunchCurrent()) { throw new InvalidOperationException("The owned worker source retired before startup."); }
                await process.StartAsync(LaunchCurrent, lifetime.Token).ConfigureAwait(false);
                using var stdout = new StreamReader(process.Output, new UTF8Encoding(false, true), false, 1024, true);
                using var stderr = new StreamReader(process.Error, new UTF8Encoding(false, true), false, 1024, true);
                await using var stdin = new StreamWriter(process.Input, new UTF8Encoding(false), 1024, true) { NewLine = "\n" };
                var errorRead = DrainBoundedAsync(stderr, lifetime.Token);
                var completed = false;
                var joined = false;
                try
                {
                    await stdin.WriteLineAsync(input.AsMemory(), lifetime.Token).ConfigureAwait(false);
                    await stdin.FlushAsync(lifetime.Token).ConfigureAwait(false);
                    var request = await ReadLineAsync(stdout, lifetime.Token).ConfigureAwait(false);
                    if (!string.Equals(request, $"Kora.FixedAction.Request.v1|{nonce}|{id}|{action}", StringComparison.Ordinal)
                        || qualification() != profile || !current() || lifetime.IsCancellationRequested)
                    { throw new InvalidDataException("The contained worker request is missing, changed, repeated or uncorrelated."); }
                    // This acknowledges only a deferred request. No Windows effect has happened.
                    await stdin.WriteLineAsync($"Kora.FixedAction.Deferred.v1|{nonce}|{id}".AsMemory(), lifetime.Token).ConfigureAwait(false);
                    await stdin.FlushAsync(lifetime.Token).ConfigureAwait(false);
                    var terminal = await ReadLineAsync(stdout, lifetime.Token).ConfigureAwait(false);
                    if (!string.Equals(terminal, $"Kora.FixedAction.Completed.v1|{nonce}|{id}", StringComparison.Ordinal))
                    { throw new InvalidDataException("The exact separate helper/entry execution did not produce its terminal protocol."); }
                    process.Input.Close();
                    if (await ReadLineAsync(stdout, lifetime.Token).ConfigureAwait(false) is not null)
                    { throw new InvalidDataException("Unexpected or repeated worker output is not an admitted effect request."); }
                    var exit = await process.JoinAsync().ConfigureAwait(false);
                    joined = true;
                    await errorRead.ConfigureAwait(false);
                    if (exit != 0 || qualification() != profile || !current() || lifetime.IsCancellationRequested)
                    { throw new InvalidOperationException("The fixed scripts or current authority did not complete; no native effect is admitted."); }
                    if (stopFailure is not null)
                    { throw new IOException("Worker cancellation has an unconfirmed stop outcome.", stopFailure); }
                    completed = true;
                    return new(invocation, script.Action, script.Package.DeclaredResourceDigest, completed, joined);
                }
                finally
                {
                    if (!joined) { process.Stop(); await process.JoinAsync().ConfigureAwait(false); }
                    await errorRead.ConfigureAwait(false);
                }
            }
            finally
            {
                await process.DisposeAsync().ConfigureAwait(false);
                Volatile.Write(ref ownedProcess, null);
                release = true;
            }
        }
        finally { if (release) { Volatile.Write(ref running, 0); } }
    }

    private static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken token)
    {
        var text = new StringBuilder();
        var buffer = new char[1];
        while (await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false) != 0)
        {
            if (buffer[0] == '\n') { return text.ToString().TrimEnd('\r'); }
            if (text.Length >= 2048) { throw new InvalidDataException("Worker output exceeded the fixed protocol bound."); }
            text.Append(buffer[0]);
        }
        return text.Length == 0 ? null : throw new InvalidDataException("Worker output ended with a partial protocol record.");
    }

    private static async Task DrainBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[512];
        var total = 0;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
        {
            total += read;
            if (total > 8192) { throw new InvalidDataException("Worker error output exceeded its bound; no output was retained."); }
        }
    }
}