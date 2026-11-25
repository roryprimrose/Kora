using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AwesomeAssertions;
using Kora.Core.Mcp;
using Kora.Windows.Mcp;
using McpProcessAuthorization = Kora.Core.Mcp.McpConnectionIdentity.McpProcessAuthorization;
using McpProcessException = Kora.Windows.Mcp.IWindowsMcpProcessLease.McpProcessException;
using McpProcessFailure = Kora.Windows.Mcp.IWindowsMcpProcessLease.McpProcessFailure;

namespace Kora.Windows.IntegrationTests;

public sealed class McpStdioTransportTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static McpConnectionIdentity Identity { get; } = new(new McpStdioEndpoint(@"C:\Installed\server.exe",
        ["--stdio"], @"C:\Workspace", new Dictionary<string, string>(StringComparer.Ordinal)));

    [Fact]
    public async Task Official_stream_transport_performs_only_admitted_handshake_list_and_call_and_joins_owned_pipes()
    {
        var process = new ProcessLease();
        var cleanup = 0;
        var connection = new McpStdioConnection(Identity, process, Authorization(() => { cleanup++; return Task.CompletedTask; }));
        (await connection.InitializeAsync(Token)).Value.Tools.Should().BeTrue();
        var tool = (await connection.ListAsync(McpCapabilityKind.Tool, null, Token)).Value.Items.Single();
        var result = await connection.InvokeAsync(tool.Id, new(McpTransportFixture.Schema), """{"query":"reference"}""", Token);
        result.Value.Text.Should().Be("inert reference");
        process.Methods.Should().Equal("initialize", "notifications/initialized", "tools/list", "tools/call");
        await connection.DisposeAsync();
        process.Released.Should().BeTrue();
        process.InputClosed.Should().BeTrue();
        process.RootStops.Should().Be(0);
        cleanup.Should().Be(0);
        connection.AdmissionWithdrawn.IsCancellationRequested.Should().BeTrue();
    }

    [Theory]
    [InlineData(65536, false)]
    [InlineData(65537, true)]
    public async Task Undecoded_stderr_retires_on_byte_65537_without_content_in_terminal_errors(int bytes, bool retired)
    {
        var process = new ProcessLease(bytes);
        var connection = new McpStdioConnection(Identity, process, Authorization());
        connection.AdmissionWithdrawn.IsCancellationRequested.Should().Be(retired);
        process.Stderr.MaximumRequested.Should().BeLessThanOrEqualTo(4096);
        process.Stderr.BytesRead.Should().Be(bytes);
        if (retired)
        {
            var error = await FluentActions.Awaiting(() => connection.InitializeAsync(Token))
                .Should().ThrowAsync<McpProcessException>();
            error.Which.Failure.Should().Be(McpProcessFailure.StderrLimit);
            error.Which.InnerException.Should().BeNull();
            error.Which.ToString().Should().NotContain("private-stderr");
            process.Methods.Should().BeEmpty();
        }
        await connection.DisposeAsync();
        process.Released.Should().BeTrue();
    }

    [Theory]
    [InlineData("server-request")]
    [InlineData("correlation")]
    [InlineData("oversized")]
    public async Task Strict_shared_admission_rejects_hostile_stdout_before_sdk_discovery(string fault)
    {
        var process = new ProcessLease { Fault = fault };
        var connection = new McpStdioConnection(Identity, process, Authorization());
        var error = await FluentActions.Awaiting(() => connection.InitializeAsync(Token))
            .Should().ThrowAsync<McpProcessException>();
        error.Which.Failure.Should().Be(McpProcessFailure.ProtocolOutput);
        error.Which.InnerException.Should().BeNull();
        process.Methods.Should().Equal("initialize");
        await connection.DisposeAsync();
        process.Released.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Eof_waits_five_seconds_then_requires_separate_current_owned_root_consent(bool allowCleanup)
    {
        var process = new ProcessLease { ExitOnClose = false };
        var clock = new Clock();
        var cleanups = 0;
        var connection = new McpStdioConnection(Identity, process, Authorization(() =>
        {
            cleanups++;
            return allowCleanup ? Task.CompletedTask : Task.FromException(new InvalidOperationException("consent retired"));
        }), clock);
        var closing = connection.DisposeAsync().AsTask();
        var grace = await clock.Timers.Reader.ReadAsync(Token);
        grace.DueTime.Should().Be(TimeSpan.FromSeconds(5));
        process.InputClosed.Should().BeTrue();
        process.RootStops.Should().Be(0);
        closing.IsCompleted.Should().BeFalse();
        grace.Tick();
        if (allowCleanup)
        {
            await closing.WaitAsync(Token);
            process.RootStops.Should().Be(1);
            process.Released.Should().BeTrue();
        }
        else
        {
            (await FluentActions.Awaiting(() => closing).Should().ThrowAsync<McpProcessException>())
                .Which.Failure.Should().Be(McpProcessFailure.ShutdownUnconfirmed);
            process.RootStops.Should().Be(0);
            process.Released.Should().BeFalse();
            process.Exit();
        }
        cleanups.Should().Be(1);
    }

    private static McpProcessAuthorization Authorization(Func<Task>? cleanup = null) =>
        new(Identity, "held-binary", static _ => Task.CompletedTask, _ => cleanup?.Invoke() ?? Task.CompletedTask);

    [Fact]
    public async Task Root_exit_does_not_release_ownership_while_a_descendant_or_ignoring_pipe_still_holds_stdout()
    {
        var process = new ProcessLease { HoldOutputAfterExit = true };
        var clock = new Clock();
        var connection = new McpStdioConnection(Identity, process, Authorization(), clock);
        await connection.InitializeAsync(Token);
        process.Fault = "hold";
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var listing = connection.ListAsync(McpCapabilityKind.Tool, null, cancellation.Token);
        await process.HeldRequest.Task.WaitAsync(Token);
        await cancellation.CancelAsync();
        await FluentActions.Awaiting(() => listing).Should().ThrowAsync<OperationCanceledException>();
        while (clock.Timers.Reader.TryRead(out _)) { }
        process.Released.Should().BeFalse();
        var closing = connection.DisposeAsync().AsTask();
        var joinDeadline = await clock.Timers.Reader.ReadAsync(Token);
        process.HasExited.Should().BeTrue();
        process.Released.Should().BeFalse();
        process.RootStops.Should().Be(0);
        closing.IsCompleted.Should().BeFalse();
        joinDeadline.Tick();
        (await FluentActions.Awaiting(() => closing).Should().ThrowAsync<McpProcessException>())
            .Which.Failure.Should().Be(McpProcessFailure.ShutdownUnconfirmed);
        process.Released.Should().BeFalse();
        ((Pipe)process.Output).Complete();
        await ((Pipe)process.Output).EofRead.Task.WaitAsync(Token);
        process.Released.Should().BeFalse();
    }

    private sealed class ProcessLease : IWindowsMcpProcessLease
    {
        private readonly TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ProcessLease(int stderrBytes = 0)
        {
            Input = new Pipe(Sent);
            Output = new Pipe();
            Stderr = new(stderrBytes);
        }
        public Stream Input { get; }
        public Stream Output { get; }
        public Stream Error => Stderr;
        internal ErrorStream Stderr { get; }
        internal List<string> Methods { get; } = [];
        internal TaskCompletionSource HeldRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string? Fault { get; set; }
        internal bool ExitOnClose { get; init; } = true;
        internal bool HoldOutputAfterExit { get; init; }
        internal bool Released { get; private set; }
        internal bool InputClosed { get; private set; }
        internal int RootStops { get; private set; }
        public bool IsOwned => !Released;
        public bool HasExited => exited.Task.IsCompleted;
        public Task WaitForExitAsync(CancellationToken token) => exited.Task.WaitAsync(token);
        public Task CloseInputAsync()
        {
            InputClosed = true;
            if (ExitOnClose) { Exit(); }
            return Task.CompletedTask;
        }
        public Task TerminateRootAsync() { RootStops++; Exit(); return Task.CompletedTask; }
        internal void Exit()
        {
            if (!HoldOutputAfterExit) { ((Pipe)Output).Complete(); }
            exited.TrySetResult();
        }
        public ValueTask DisposeAsync()
        {
            HasExited.Should().BeTrue();
            Released = true;
            return ValueTask.CompletedTask;
        }
        private void Sent(byte[] bytes)
        {
            using var request = JsonDocument.Parse(bytes);
            var method = request.RootElement.GetProperty("method").GetString()!;
            Methods.Add(method);
            if (method is "notifications/initialized") { return; }
            if (Fault is "hold") { HeldRequest.TrySetResult(); return; }
            string response;
            if (Fault is "server-request") { response = """{"jsonrpc":"2.0","id":9,"method":"sampling/createMessage","params":{}}"""; }
            else if (Fault is "correlation") { response = McpTransportFixture.Initialization.Replace("\"id\":1", "\"id\":99", StringComparison.Ordinal); }
            else if (Fault is "oversized") { response = new string('x', McpProtocol.MaximumDiscoveryUtf8Bytes + 1); }
            else if (method is "initialize") { response = McpTransportFixture.Initialization; }
            else
            {
                var id = request.RootElement.GetProperty("id").GetInt32();
                response = method is "tools/list"
                    ? JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result = new { tools = new[] {
                        new { name = "search", inputSchema = JsonSerializer.Deserialize<JsonElement>(McpTransportFixture.Schema) } } } })
                    : JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result = new {
                        content = new[] { new { type = "text", text = "inert reference" } } } });
            }
            ((Pipe)Output).Enqueue(Encoding.UTF8.GetBytes(response + "\n"));
        }
    }

    private sealed class ErrorStream(int bytes) : MemoryStream(new byte[bytes])
    {
        internal int MaximumRequested { get; private set; }
        internal int BytesRead { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            MaximumRequested = Math.Max(MaximumRequested, buffer.Length);
            var count = Read(buffer.Span);
            BytesRead += count;
            return ValueTask.FromResult(count);
        }
    }

    private sealed class Pipe(Action<byte[]>? written = null) : Stream
    {
        private readonly Channel<byte[]> queue = Channel.CreateUnbounded<byte[]>();
        internal TaskCompletionSource EofRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private byte[] current = [];
        private int offset;
        internal void Enqueue(byte[] bytes) => queue.Writer.TryWrite(bytes).Should().BeTrue();
        internal void Complete() => queue.Writer.TryComplete();
        public override bool CanRead => written is null;
        public override bool CanWrite => written is not null;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            written!(buffer.ToArray());
            return ValueTask.CompletedTask;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (offset == current.Length)
            {
                if (!await queue.Reader.WaitToReadAsync(cancellationToken)) { EofRead.TrySetResult(); return 0; }
                current = await queue.Reader.ReadAsync(cancellationToken);
                offset = 0;
            }
            var count = Math.Min(current.Length - offset, buffer.Length);
            current.AsMemory(offset, count).CopyTo(buffer);
            offset += count;
            return count;
        }
    }

    private sealed class Clock : TimeProvider
    {
        internal Channel<Timer> Timers { get; } = Channel.CreateUnbounded<Timer>();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(callback, state, dueTime);
            Timers.Writer.TryWrite(timer).Should().BeTrue();
            return timer;
        }
        internal sealed class Timer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
        {
            internal TimeSpan DueTime => dueTime;
            internal void Tick() => callback(state);
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
