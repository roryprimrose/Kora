using System.Net;
using Kora.Core.Mcp;
using ModelContextProtocol.Protocol;
using McpProcessAuthorization = Kora.Core.Mcp.McpConnectionIdentity.McpProcessAuthorization;
using McpProcessFailure = Kora.Windows.Mcp.IWindowsMcpProcessLease.McpProcessFailure;
using McpProcessException = Kora.Windows.Mcp.IWindowsMcpProcessLease.McpProcessException;

namespace Kora.Windows.Mcp;

internal sealed class McpStdioConnection : IMcpConnection
{
    private readonly IWindowsMcpProcessLease process;
    private readonly McpProcessAuthorization authorization;
    private readonly TimeProvider time;
    private readonly McpSdkConnection sdk;
    private readonly ProtocolStream input;
    private readonly ProtocolStream output;
    private readonly CancellationTokenSource retired = new();
    private readonly CancellationToken withdrawal;
    private readonly SemaphoreSlim serial = new(1, 1);
    private readonly Lock gate = new();
    private readonly HashSet<Task> active = [];
    private readonly Task errorDrain;
    private readonly Task processExit;
    private Task? disposal;
    private Task? physicalJoin;
    private McpProcessFailure? failure;

    internal McpStdioConnection(McpConnectionIdentity identity, IWindowsMcpProcessLease process,
        McpProcessAuthorization authorization, TimeProvider? time = null)
    {
        Identity = identity;
        this.process = process;
        this.authorization = authorization;
        this.time = time ?? TimeProvider.System;
        withdrawal = retired.Token;
        var admission = new McpSdkRequestHandler();
        input = new(process.Input, admission, read: false);
        output = new(process.Output, admission, read: true);
        sdk = new(admission, () => new StreamClientTransport(input, output),
            RequireCurrentAsync, this.time);
        errorDrain = DrainErrorAsync();
        processExit = ObserveExitAsync();
    }

    public McpConnectionIdentity Identity { get; }
    public McpAccountReference Account => throw new InvalidOperationException("Stdio has no delegated account.");
    public McpEndpoint Endpoint => throw new InvalidOperationException("Stdio has no HTTPS endpoint.");
    public IReadOnlyList<IPAddress> RemoteAddresses => [];
    public CancellationToken AdmissionWithdrawn => withdrawal;
    public Task<bool> IsCurrentAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(!withdrawal.IsCancellationRequested && process.IsOwned && !process.HasExited);
    }

    private Task RequireCurrentAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (failure is { } terminal) { throw new McpProcessException(terminal); }
        if (withdrawal.IsCancellationRequested || !process.IsOwned || process.HasExited)
        { throw new InvalidOperationException("The exact owned MCP process is no longer admitted."); }
        return Task.CompletedTask;
    }

    public Task<McpConnectionReply<McpServerCapabilities>> InitializeAsync(CancellationToken token) =>
        RunAsync(sdk.InitializeAsync, token);
    public Task<McpConnectionReply<McpCapabilityPage>> ListAsync(McpCapabilityKind kind, string? cursor, CancellationToken token) =>
        RunAsync(current => sdk.ListAsync(kind, cursor, current), token);
    public Task<McpConnectionReply<McpRemoteResult>> InvokeAsync(string name, McpToolSchema schema, string arguments, CancellationToken token) =>
        RunAsync(current => sdk.InvokeAsync(name, schema, arguments, current), token);
    public Task<McpConnectionReply<McpTextDocument>> ReadResourceAsync(string uri, CancellationToken token) =>
        RunAsync(current => sdk.ReadResourceAsync(uri, current), token);
    public Task<McpConnectionReply<McpTextDocument>> ReadPromptAsync(McpCapability prompt, string arguments, CancellationToken token) =>
        RunAsync(current => sdk.ReadPromptAsync(prompt, arguments, current), token);

    private Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposal is not null, this);
            active.Add(completion.Task);
        }
        return ExecuteAsync();
        async Task<T> ExecuteAsync()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, withdrawal, deadline.Token);
            try
            {
                await serial.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    await RequireCurrentAsync(linked.Token).ConfigureAwait(false);
                    return await operation(linked.Token).ConfigureAwait(false);
                }
                finally { serial.Release(); }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                lock (gate) { failure ??= McpProcessFailure.ProtocolOutput; }
                await retired.CancelAsync().ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                throw new McpProcessException(failure.Value);
            }
            finally
            {
                lock (gate) { completion.SetResult(); active.Remove(completion.Task); }
            }
        }
    }

    private async Task DrainErrorAsync()
    {
        var buffer = new byte[4096];
        var total = 0;
        try
        {
            while (true)
            {
                var read = await process.Error.ReadAsync(buffer.AsMemory(0, Math.Min(4096, 65537 - total)),
                    CancellationToken.None).ConfigureAwait(false);
                if (read == 0) { return; }
                total += read;
                if (total > 65536)
                {
                    lock (gate) { failure = McpProcessFailure.StderrLimit; }
                    await retired.CancelAsync().ConfigureAwait(false);
                    return;
                }

            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            lock (gate) { failure ??= McpProcessFailure.ProtocolOutput; }
            await retired.CancelAsync().ConfigureAwait(false);
            throw new McpProcessException(failure.Value);
        }
    }

    private async Task ObserveExitAsync()
    {
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        await retired.CancelAsync().ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        lock (gate) { return new(disposal ??= DisposeCoreAsync([.. active])); }
    }

    private async Task DisposeCoreAsync(Task[] calls)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await retired.CancelAsync().ConfigureAwait(false);
        await process.CloseInputAsync().ConfigureAwait(false);
        var exit = process.WaitForExitAsync(CancellationToken.None);
        if (!await WithinGraceAsync(exit).ConfigureAwait(false))
        {
            if (!process.IsOwned) { throw new McpProcessException(McpProcessFailure.ShutdownUnconfirmed); }
            try
            {
                await authorization.ValidateCleanupAsync(CancellationToken.None).ConfigureAwait(false);
                if (!process.IsOwned) { throw new McpProcessException(McpProcessFailure.ShutdownUnconfirmed); }
                await process.TerminateRootAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            { throw new McpProcessException(McpProcessFailure.ShutdownUnconfirmed); }
            if (!await WithinGraceAsync(exit).ConfigureAwait(false) || !process.HasExited)
            { throw new McpProcessException(McpProcessFailure.ShutdownUnconfirmed); }
        }
        physicalJoin = JoinPhysicalAsync(calls);
        if (!await WithinGraceAsync(physicalJoin).ConfigureAwait(false))
        { throw new McpProcessException(McpProcessFailure.ShutdownUnconfirmed); }
        await physicalJoin.ConfigureAwait(false);
        if (!process.HasExited) { throw new McpProcessException(McpProcessFailure.ShutdownUnconfirmed); }
        await process.DisposeAsync().ConfigureAwait(false);
        retired.Dispose();
        serial.Dispose();
    }

    private async Task JoinPhysicalAsync(Task[] calls)
    {
        await Task.WhenAll(calls).ConfigureAwait(false);
        await sdk.DisposeAsync().ConfigureAwait(false);
        await input.JoinAsync().ConfigureAwait(false);
        await output.DrainAsync().ConfigureAwait(false);
#pragma warning disable VSTHRD003 // Joins this connection's exact already-owned physical stderr drain.
        await errorDrain.ConfigureAwait(false);
        await processExit.ConfigureAwait(false);
#pragma warning restore VSTHRD003
    }

    private async Task<bool> WithinGraceAsync(Task operation)
    {
#pragma warning disable VSTHRD003 // Joins only this owner's exact completed root/physical-stream operation.
        if (operation.IsCompleted) { await operation.ConfigureAwait(false); return true; }
#pragma warning restore VSTHRD003
        using var elapsed = new CancellationTokenSource(TimeSpan.FromSeconds(5), time);
        try { await operation.WaitAsync(elapsed.Token).ConfigureAwait(false); return true; }
        catch (OperationCanceledException) when (elapsed.IsCancellationRequested) { return false; }
    }

    private sealed class ProtocolStream(Stream physical, McpSdkRequestHandler admission, bool read) : Stream
    {
        private readonly SemaphoreSlim io = new(1, 1);
        private readonly byte[] chunk = new byte[4096];
        private int chunkOffset;
        private int chunkCount;
        private byte[] frame = [];
        private int frameOffset;
        private readonly MemoryStream outgoing = new();
        public override bool CanRead => read;
        public override bool CanWrite => !read;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await io.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { await physical.FlushAsync(CancellationToken.None).ConfigureAwait(false); }
            finally { io.Release(); }
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await io.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                for (var index = 0; index < buffer.Length; index++)
                {
                    var value = buffer.Span[index];
                    if (value == (byte)'\n')
                    {
                        var notification = admission.AdmitRequest(outgoing.ToArray());
                        outgoing.WriteByte(value);
                        await physical.WriteAsync(outgoing.GetBuffer().AsMemory(0, checked((int)outgoing.Length)),
                            CancellationToken.None).ConfigureAwait(false);
                        outgoing.SetLength(0);
                        if (notification) { admission.CommitNotification(); }
                    }
                    else
                    {
                        if (outgoing.Length >= McpProtocol.MaximumDiscoveryUtf8Bytes)
                        { throw new InvalidDataException("The complete SDK MCP request exceeds its byte bound."); }
                        outgoing.WriteByte(value);
                    }
                }
            }
            finally { io.Release(); }
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.IsEmpty) { return 0; }
            await io.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (frameOffset == frame.Length)
                {
                    using var line = new MemoryStream();
                    while (true)
                    {
                        if (chunkOffset == chunkCount)
                        {
                            chunkCount = await physical.ReadAsync(chunk, CancellationToken.None).ConfigureAwait(false);
                            chunkOffset = 0;
                            if (chunkCount == 0)
                            {
                                if (line.Length != 0) { throw new InvalidDataException("The MCP frame ended before newline."); }
                                return 0;
                            }
                        }
                        var value = chunk[chunkOffset++];
                        if (value == (byte)'\n')
                        {
                            var bytes = line.ToArray();
                            admission.AdmitResponse(bytes);
                            frame = [.. bytes, (byte)'\n'];
                            frameOffset = 0;
                            break;
                        }
                        if (line.Length >= admission.MaximumResponseBytes)
                        { throw new InvalidDataException("The complete MCP response exceeds the admitted byte bound."); }
                        line.WriteByte(value);
                    }
                }
                var count = Math.Min(buffer.Length, frame.Length - frameOffset);
                frame.AsMemory(frameOffset, count).CopyTo(buffer);
                frameOffset += count;
                return count;
            }
            finally { io.Release(); }
        }

        internal async Task JoinAsync()
        {
            await io.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            io.Release();
        }

        internal async Task DrainAsync()
        {
            await io.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                while (await physical.ReadAsync(chunk, CancellationToken.None).ConfigureAwait(false) != 0) { }
            }
            finally { io.Release(); }
        }

        protected override void Dispose(bool disposing) => base.Dispose(disposing);
    }
}
