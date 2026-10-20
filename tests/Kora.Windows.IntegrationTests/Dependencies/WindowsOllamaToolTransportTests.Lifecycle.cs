using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AwesomeAssertions;
using Kora.Core.Dependencies;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class WindowsOllamaToolTransportTests
{
    [Fact]
    public async Task Pre_cancelled_request_sends_nothing_and_has_no_success_payload()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var reply = await fixture.Transport.CompleteAsync(fixture.Request(), cancellation.Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        reply.Message.Should().BeNull();
        fixture.Paths.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_during_noncooperative_read_keeps_resources_owned_until_real_read_termination(bool dispose)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        using var cancellation = new CancellationTokenSource();
        var stream = new ControlledStream(Encoding.UTF8.GetBytes(Final));
        fixture.Chat = (message, _) => Task.FromResult(Streaming(message, stream));
        var pending = fixture.Transport.CompleteAsync(fixture.Request(), cancellation.Token);
        Task? disposal = null;
        try
        {
            await stream.Entered.Task.WaitAsync(Token);
            if (dispose) { disposal = fixture.Transport.DisposeAsync().AsTask(); }
            else { await cancellation.CancelAsync(); }
            await stream.CancellationObserved.Task.WaitAsync(Token);
            pending.IsCompleted.Should().BeFalse();
            if (disposal is not null) { disposal.IsCompleted.Should().BeFalse(); }
            fixture.HttpHandler.Disposed.Should().BeFalse();
        }
        finally { stream.Release.TrySetResult(); }
        var reply = await pending;
        reply.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        reply.Message.Should().BeNull();
        stream.Disposed.Should().BeTrue();
        if (disposal is not null) { await disposal; fixture.HttpHandler.Disposed.Should().BeTrue(); }
    }

    [Fact]
    public async Task Read_cancellation_is_passed_to_the_stream_and_retains_no_partial_answer()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        using var cancellation = new CancellationTokenSource();
        var stream = new ControlledStream(Encoding.UTF8.GetBytes(Final), honorCancellation: true);
        fixture.Chat = (message, _) => Task.FromResult(Streaming(message, stream));
        var pending = fixture.Transport.CompleteAsync(fixture.Request(), cancellation.Token);
        await stream.Entered.Task.WaitAsync(Token);
        await cancellation.CancelAsync();
        var reply = await pending;
        reply.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        reply.Message.Should().BeNull();
        stream.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Deadline_and_late_success_are_independent_of_provider_cooperation()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Chat = (message, _) =>
        {
            fixture.Time.Now += ModelToolProtocol.Deadline;
            return Task.FromResult(Json(message, Final));
        };
        var reply = await fixture.Transport.CompleteAsync(fixture.Request(), Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.TimedOut);
        reply.Message.Should().BeNull();
    }

    [Theory]
    [InlineData("admission")]
    [InlineData("owner")]
    [InlineData("revocation")]
    public async Task Revoked_or_drifted_binding_suppresses_a_late_complete_provider_answer(string failure)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Chat = async (message, _) =>
        {
            if (string.Equals(failure, "admission", StringComparison.Ordinal)) { fixture.Admitted = false; }
            if (string.Equals(failure, "owner", StringComparison.Ordinal)) { fixture.ProcessCurrent = false; }
            if (string.Equals(failure, "revocation", StringComparison.Ordinal)) { await fixture.Revoked.CancelAsync(); }
            return Json(message, Final);
        };
        var reply = await fixture.Transport.CompleteAsync(fixture.Request(), Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Denied);
        reply.Message.Should().BeNull();
    }

    [Fact]
    public async Task Streaming_unknown_length_overflow_reads_only_limit_plus_one_and_never_truncates_to_success()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        var stream = new ControlledStream(Encoding.UTF8.GetBytes(new string(' ', ModelToolProtocol.MaximumOutputUtf8Bytes + 20)));
        stream.Release.TrySetResult();
        fixture.Chat = (message, _) => Task.FromResult(Streaming(message, stream));
        var reply = await fixture.Transport.CompleteAsync(fixture.Request(), Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Denied);
        reply.Message.Should().BeNull();
        stream.ReadBytes.Should().Be(ModelToolProtocol.MaximumOutputUtf8Bytes + 1);
        stream.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task A_concurrent_exchange_cannot_start_a_second_HTTP_request()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        var stream = new ControlledStream(Encoding.UTF8.GetBytes(Final));
        fixture.Chat = (message, _) => Task.FromResult(Streaming(message, stream));
        var request = fixture.Request();
        var pending = fixture.Transport.CompleteAsync(request, Token);
        try
        {
            await stream.Entered.Task.WaitAsync(Token);
            (await fixture.Transport.CompleteAsync(request, Token)).Outcome.Should().Be(ModelTurnOutcome.Denied);
            fixture.Bodies.Should().ContainSingle();
        }
        finally { stream.Release.TrySetResult(); }
        (await pending).Outcome.Should().Be(ModelTurnOutcome.Succeeded);
    }

    [Theory]
    [InlineData("redirect")]
    [InlineData("foreign-response-uri")]
    [InlineData("compressed")]
    [InlineData("charset")]
    [InlineData("media-type")]
    public async Task Metadata_transport_cannot_redirect_substitute_response_source_or_decode_opaque_content(string failure)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Metadata = (message, _) =>
        {
            var response = Json(message, """{"version":"fake-runtime"}""");
            switch (failure)
            {
                case "redirect": response.StatusCode = HttpStatusCode.Found; break;
                case "foreign-response-uri": response.RequestMessage = new(HttpMethod.Get, "http://untrusted.invalid/"); break;
                case "compressed": response.Content.Headers.ContentEncoding.Add("gzip"); break;
                case "charset": response.Content.Headers.ContentType!.CharSet = "utf-16"; break;
                case "media-type": response.Content.Headers.ContentType!.MediaType = "text/html"; break;
            }
            return Task.FromResult(response);
        };
        var reply = await fixture.Transport.CompleteAsync(fixture.Request(), Token);
        reply.Outcome.Should().Be(failure is "redirect" or "foreign-response-uri"
            ? ModelTurnOutcome.DeniedEgress : ModelTurnOutcome.Denied);
        fixture.Bodies.Should().BeEmpty();
    }

    private static HttpResponseMessage Streaming(HttpRequestMessage request, Stream stream)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new StreamContent(stream) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return response;
    }

    private sealed class ControlledStream(byte[] bytes, bool honorCancellation = false) : Stream
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource CancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed;
        internal int ReadBytes;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            using var registration = cancellationToken.Register(() => CancellationObserved.TrySetResult());
            Entered.TrySetResult();
            if (honorCancellation) { await Release.Task.WaitAsync(cancellationToken); }
            else { await Release.Task; }
            var length = Math.Min(buffer.Length, bytes.Length - ReadBytes);
            bytes.AsMemory(ReadBytes, length).CopyTo(buffer);
            ReadBytes += length;
            return length;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
