using AwesomeAssertions;

using Kora.Windows.Coordination;

namespace Kora.Windows.IntegrationTests;

public sealed class InstallerShutdownHandshakeTests
{
    [Theory]
    [InlineData("Denied")]
    [InlineData("Declined")]
    [InlineData("Busy")]
    [InlineData("Activated")]
    [InlineData("Ready")]
    public async Task LegacyOrDeniedReplyFailsExplicitlyWithoutWaitingForOrStoppingTheOwner(string response)
    {
        using var pipe = new DuplexStream(await FramesAsync(new InstanceMessage(InstanceProtocol.Version, response)));
        var waits = 0;
        var request = () => InstallerShutdownHandshake.RequestStopAsync(pipe, _ =>
        {
            waits++;
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
        await request.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Close Kora normally*older releases*");
        waits.Should().Be(0);
        using var sent = new MemoryStream(pipe.Requests);
        var message = await InstanceProtocol.ReadAsync(sent, TestContext.Current.CancellationToken);
        message.Kind.Should().Be("InstallerStop");
        message.Epoch.Should().BeEmpty();
        message.Transaction.Should().BeEmpty();
        message.Ticket.Should().BeEmpty();
        message.Detail.Should().BeEmpty();
    }

    [Fact]
    public async Task PipeEofIsNotProofOfProcessExit()
    {
        using var pipe = new DuplexStream(await FramesAsync(Acknowledged()));
        var exit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waits = 0;
        var request = InstallerShutdownHandshake.RequestStopAsync(pipe, token =>
        {
            waits++;
            return exit.Task.WaitAsync(token);
        }, TestContext.Current.CancellationToken);
        waits.Should().Be(1);
        request.IsCompleted.Should().BeFalse();
        exit.SetResult();
        await request;
    }

    [Theory]
    [InlineData("Busy", false)]
    [InlineData("Declined", false)]
    [InlineData("Busy", true)]
    public async Task TerminalDenialCannotBecomeSuccessEvenIfTheProcessAlreadyExited(string response, bool alreadyExited)
    {
        using var pipe = new DuplexStream(await FramesAsync(Acknowledged(),
            new InstanceMessage(InstanceProtocol.Version, response)));
        var exit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (alreadyExited) { exit.SetResult(); }
        var request = () => InstallerShutdownHandshake.RequestStopAsync(pipe,
            token => exit.Task.WaitAsync(token), TestContext.Current.CancellationToken);
        await request.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No package changes*");
    }

    [Fact]
    public async Task CancellationWhileWaitingForActualExitCannotAdmitShutdown()
    {
        using var cancellation = new CancellationTokenSource();
        using var pipe = new DuplexStream(await FramesAsync(Acknowledged()));
        var exit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = InstallerShutdownHandshake.RequestStopAsync(pipe,
            token => exit.Task.WaitAsync(token), cancellation.Token);
        task.IsCompleted.Should().BeFalse();
        await cancellation.CancelAsync();
        var request = () => task;
        await request.Should().ThrowAsync<OperationCanceledException>();
        exit.Task.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task ExitMonitoringFailureIsNotCleanShutdown()
    {
        using var pipe = new DuplexStream(await FramesAsync(Acknowledged()));
        var request = () => InstallerShutdownHandshake.RequestStopAsync(pipe,
            _ => Task.FromException(new InvalidOperationException("Exit could not be confirmed.")),
            TestContext.Current.CancellationToken);
        await request.Should().ThrowAsync<InvalidOperationException>().WithMessage("Exit could not be confirmed.");
    }

    [Fact]
    public async Task UnexpectedAcknowledgementAuthorityPayloadIsRejected()
    {
        using var pipe = new DuplexStream(await FramesAsync(Acknowledged() with { Ticket = new string('a', 64) }));
        var waits = 0;
        var request = () => InstallerShutdownHandshake.RequestStopAsync(pipe, _ =>
        {
            waits++;
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
        await request.Should().ThrowAsync<InvalidOperationException>();
        waits.Should().Be(0);
    }

    private static InstanceMessage Acknowledged() => new(InstanceProtocol.Version, "StopRequested");

    private static async Task<byte[]> FramesAsync(params InstanceMessage[] messages)
    {
        using var stream = new MemoryStream();
        foreach (var message in messages)
        {
            await InstanceProtocol.WriteAsync(stream, message, TestContext.Current.CancellationToken);
        }
        return stream.ToArray();
    }

    private sealed class DuplexStream(byte[] responses) : Stream
    {
        private readonly MemoryStream reader = new(responses);
        private readonly MemoryStream writer = new();
        public byte[] Requests => writer.ToArray();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => writer.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => writer.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => reader.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => reader.ReadAsync(buffer, cancellationToken);
        public override void Write(byte[] buffer, int offset, int count) => writer.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => writer.WriteAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) { reader.Dispose(); writer.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
