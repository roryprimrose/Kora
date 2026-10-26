using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Windows.Dependencies;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class WindowsOllamaToolTransportTests
{
    private static string AnswerChunk(string text, bool done = false) => JsonSerializer.Serialize(new
    {
        model = Model,
        message = new { role = "assistant", content = text },
        done,
    });
    private static string AnswerTerminal => AnswerChunk(string.Empty, true).Replace(
        "\"done\":true", "\"done\":true,\"done_reason\":\"stop\"", StringComparison.Ordinal);

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(7, false)]
    [InlineData(4096, true)]
    public async Task NativeAnswerStreamPublishesDecodedBoundedTextBeforeTerminalAndJoinsCompleteValidation(
        int segment, bool rawUtf8)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        const string text = "answer \U0001F642 \u00e9 \"quoted\" \\ end";
        var first = AnswerChunk(text) + "\n";
        if (rawUtf8)
        {
            first = first.Replace("\\uD83D\\uDE42", "\U0001F642", StringComparison.Ordinal)
                .Replace("\\u00E9", "\u00e9", StringComparison.Ordinal);
        }
        var wire = first + AnswerChunk(" complete") + "\n" + AnswerTerminal;
        var stream = new AnswerStream(Encoding.UTF8.GetBytes(wire), segment, Encoding.UTF8.GetByteCount(first));
        f.Chat = (message, _) => Task.FromResult(AnswerStreaming(message, stream));
        var updates = new List<ModelAnswerUpdate>();
        var request = f.Request(toolMode: ModelToolMode.None);
        var pending = f.Transport.CompleteWithAnswerAsync(request, (update, _) =>
        {
            updates.Add(update);
            return ValueTask.CompletedTask;
        }, Token);
        await stream.Paused.Task.WaitAsync(Token);
        try
        {
            updates.Should().ContainSingle().Which.Answer.Should().Be(text);
            updates[0].Provenance.Should().BeSameAs(f.Provenance);
            pending.IsCompleted.Should().BeFalse("a provisional answer is not a completed response");
            using var body = JsonDocument.Parse(f.Bodies.Single());
            body.RootElement.GetProperty("stream").GetBoolean().Should().BeTrue();
            body.RootElement.GetProperty("think").GetBoolean().Should().BeFalse();
            body.RootElement.TryGetProperty("tools", out _).Should().BeFalse();
        }
        finally { stream.Release.TrySetResult(); }
        var reply = await pending;
        reply.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        reply.Message.Should().BeOfType<ModelFinalResponse>().Which.Answer.Should().Be(text + " complete");
        updates.Select(update => update.Answer).Should().Equal(text, text + " complete");
        stream.Disposed.Should().BeTrue();
        reply.Usage!.OutputUtf8Bytes.Should().Be(Encoding.UTF8.GetByteCount(wire));
    }

    [Theory]
    [InlineData("truncated")]
    [InlineData("duplicate-terminal")]
    [InlineData("after-terminal")]
    [InlineData("missing-reason")]
    [InlineData("length")]
    [InlineData("wrong-model")]
    [InlineData("thinking")]
    [InlineData("tools")]
    [InlineData("authority")]
    [InlineData("duplicate-field")]
    [InlineData("wrong-order")]
    [InlineData("malformed")]
    [InlineData("surrogate")]
    [InlineData("blank-line")]
    [InlineData("bad-done")]
    [InlineData("array")]
    [InlineData("null")]
    [InlineData("missing-done")]
    [InlineData("invalid-utf8")]
    public async Task InvalidOrIncompleteNativeStreamNeverBecomesPartialSuccessOrLeaksEnvelopeMetadata(string fault)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        var chunk = AnswerChunk("safe provisional");
        var terminal = AnswerTerminal;
        var wire = fault switch
        {
            "truncated" => chunk + "\n",
            "duplicate-terminal" => chunk + "\n" + terminal + "\n" + terminal,
            "after-terminal" => terminal + "\n" + chunk,
            "missing-reason" => chunk + "\n" + AnswerChunk(string.Empty, true),
            "length" => chunk + "\n" + terminal.Replace("stop", "length", StringComparison.Ordinal),
            "wrong-model" => chunk.Replace(Model, "foreign", StringComparison.Ordinal) + "\n" + terminal,
            "thinking" => chunk.Replace("\"content\":", "\"thinking\":\"hidden reasoning\",\"content\":", StringComparison.Ordinal),
            "tools" => chunk.Replace("\"content\":", "\"tool_calls\":[],\"content\":", StringComparison.Ordinal),
            "authority" => chunk.Replace("\"content\":", "\"authority\":\"grant\",\"content\":", StringComparison.Ordinal),
            "duplicate-field" => chunk.Replace("\"done\":false", "\"done\":false,\"done\":false", StringComparison.Ordinal),
            "wrong-order" => chunk.Replace("\"done\":false", "\"done\":false,\"done_reason\":\"stop\"", StringComparison.Ordinal),
            "malformed" => chunk[..^1],
            "surrogate" => chunk.Replace("safe provisional", "\\uD83D", StringComparison.Ordinal),
            "blank-line" => "\n" + terminal,
            "array" => "[]",
            "null" => "null",
            "missing-done" => """{"model":"deterministic-fake:one","message":{"role":"assistant","content":"not admitted"}}""",
            "invalid-utf8" => chunk,
            _ => chunk.Replace("\"done\":false", "\"done\":0", StringComparison.Ordinal),
        };
        var bytes = Encoding.UTF8.GetBytes(wire);
        if (fault is "invalid-utf8") { bytes[0] = 0xff; }
        var stream = new AnswerStream(bytes, 1);
        f.Chat = (message, _) => Task.FromResult(AnswerStreaming(message, stream));
        var updates = new List<string>();
        var reply = await f.Transport.CompleteWithAnswerAsync(f.Request(toolMode: ModelToolMode.None), (update, _) =>
        {
            updates.Add(update.Answer);
            return ValueTask.CompletedTask;
        }, Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Denied);
        reply.Message.Should().BeNull();
        reply.Usage.Should().BeNull();
        updates.Should().OnlyContain(value => value == "safe provisional");
        stream.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(-1, true)]
    public async Task NativeStreamChargesCumulativeWireAtExactInclusiveLimit(int adjustment, bool knownLength)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        var wire = AnswerChunk(new string('x', 50)) + "\n" + AnswerChunk(new string('y', 50)) + "\n" + AnswerTerminal + "\n";
        var bytes = Encoding.UTF8.GetBytes(wire);
        var stream = new AnswerStream(bytes, 7);
        f.Chat = (message, _) =>
        {
            var response = AnswerStreaming(message, stream);
            if (knownLength) { response.Content.Headers.ContentLength = bytes.Length; }
            return Task.FromResult(response);
        };
        var reply = await f.Transport.CompleteWithAnswerAsync(f.Request(toolMode: ModelToolMode.None,
            budget: new(ModelToolProtocol.MaximumInputUtf8Bytes, bytes.Length + adjustment)),
            static (_, _) => ValueTask.CompletedTask, Token);
        reply.Outcome.Should().Be(adjustment == 0 ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.Denied);
        if (adjustment == 0) { reply.Usage!.OutputUtf8Bytes.Should().Be(bytes.Length); }
        else { reply.Message.Should().BeNull(); }
        stream.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeStreamRevocationStopsPublicationAndCancellationIgnoringReadRetainsDisposalOwnership(bool revoke)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        var first = AnswerChunk("provisional") + "\n";
        var stream = new AnswerStream(Encoding.UTF8.GetBytes(first + AnswerTerminal), 3, Encoding.UTF8.GetByteCount(first));
        f.Chat = (message, _) => Task.FromResult(AnswerStreaming(message, stream));
        var count = 0;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var pending = f.Transport.CompleteWithAnswerAsync(f.Request(toolMode: ModelToolMode.None), (_, _) =>
        {
            count++;
            return ValueTask.CompletedTask;
        }, cancellation.Token);
        await stream.Paused.Task.WaitAsync(Token);
        if (revoke) { await f.Revoked.CancelAsync(); }
        else { await cancellation.CancelAsync(); }
        var disposal = f.Transport.DisposeAsync().AsTask();
        pending.IsCompleted.Should().BeFalse();
        disposal.IsCompleted.Should().BeFalse();
        stream.Release.TrySetResult();
        var reply = await pending;
        await disposal;
        reply.Outcome.Should().Be(revoke ? ModelTurnOutcome.Denied : ModelTurnOutcome.Cancelled);
        reply.Message.Should().BeNull();
        count.Should().Be(1);
        stream.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task NativeStreamingOptionPreservesBufferedToolExchangeWithoutPublishingToolMetadata()
    {
        await using var f = new Fixture();
        using var root = f.Root();
        f.Chat = (message, _) => Task.FromResult(Json(message, Call));
        var updates = 0;
        var reply = await f.Transport.CompleteWithAnswerAsync(f.Request(), (_, _) =>
        {
            updates++;
            return ValueTask.CompletedTask;
        }, Token);
        reply.Message.Should().BeOfType<ModelAssistantToolCalls>();
        updates.Should().Be(0);
        using var body = JsonDocument.Parse(f.Bodies.Single());
        body.RootElement.GetProperty("stream").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NativeStreamingInputLimitIncludesCompleteRequestFramingBeforeAnyHttp(int adjustment)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        var initial = f.Request(toolMode: ModelToolMode.None);
        var canonical = ModelToolProtocol.SerializeRequest(initial).Length;
        f.BoundModel = Model + new string('m', canonical);
        var native = OllamaToolWire.WriteRequest(initial, f.BoundModel, [], streaming: true).Length;
        native.Should().BeGreaterThan(canonical);
        f.Chat = (message, _) => Task.FromResult(Json(message, NativeAnswer(f.BoundModel)));
        var request = f.Request(toolMode: ModelToolMode.None,
            budget: new(native + adjustment, ModelToolProtocol.MaximumOutputUtf8Bytes));
        var result = await f.Transport.CompleteWithAnswerAsync(request, static (_, _) => ValueTask.CompletedTask, Token);
        result.Outcome.Should().Be(adjustment == 0 ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.Denied);
        if (adjustment == 0) { result.Usage!.InputUtf8Bytes.Should().Be(native); }
        else { f.Paths.Should().BeEmpty(); }
    }

    private static HttpResponseMessage AnswerStreaming(HttpRequestMessage message, Stream stream)
    {
        var response = Streaming(message, stream);
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");
        return response;
    }

    private sealed class AnswerStream(byte[] bytes, int segment, int pauseAt = int.MaxValue) : Stream
    {
        internal TaskCompletionSource Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed { get; private set; }
        private int offset;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (offset >= pauseAt && !Release.Task.IsCompleted)
            {
                Paused.TrySetResult();
                await Release.Task;
            }
            var count = Math.Min(Math.Min(buffer.Length, segment), bytes.Length - offset);
            if (offset < pauseAt) { count = Math.Min(count, pauseAt - offset); }
            bytes.AsMemory(offset, count).CopyTo(buffer);
            offset += count;
            return count;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
