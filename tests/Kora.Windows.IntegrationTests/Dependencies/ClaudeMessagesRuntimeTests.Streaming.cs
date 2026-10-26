using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class ClaudeMessagesRuntimeTests
{
    internal static string Event(string type, object payload) =>
        "event: " + type + "\ndata: " + JsonSerializer.Serialize(payload) + "\n\n";
    internal static string StreamStart => Event("message_start", new
    {
        type = "message_start",
        message = new { id = "msg_test", type = "message", role = "assistant", model = "exact-model",
            content = Array.Empty<object>(), stop_reason = (string?)null, stop_sequence = (string?)null,
            usage = new { input_tokens = 25, output_tokens = 1 } },
    }) + Event("content_block_start", new { type = "content_block_start", index = 0, content_block = new { type = "text", text = "" } });
    internal static string StreamText(string text) =>
        Event("content_block_delta", new { type = "content_block_delta", index = 0, delta = new { type = "text_delta", text } });
    internal static string StreamEnd => Event("content_block_stop", new { type = "content_block_stop", index = 0 })
        + Event("message_delta", new { type = "message_delta", delta = new { stop_reason = "end_turn", stop_sequence = (string?)null },
            usage = new { output_tokens = 5 } }) + Event("message_stop", new { type = "message_stop" });

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(7, false)]
    [InlineData(4096, true)]
    public async Task NativeClaudePublishesDecodedTextBeforeTerminalBytesAndKeepsEachPhysicalPostReviewed(int segment, bool rawUtf8)
    {
        await using var f = await StreamFixture.CreateAsync();
        const string text = "answer \U0001F642 \u00e9 \"quoted\" \\ end";
        var first = StreamStart + StreamText(text);
        if (rawUtf8)
        {
            first = first.Replace("\\uD83D\\uDE42", "\U0001F642", StringComparison.Ordinal)
                .Replace("\\u00E9", "\u00e9", StringComparison.Ordinal);
        }
        var stream = f.Respond(first + StreamText(" complete") + StreamEnd, segment, Encoding.UTF8.GetByteCount(first));
        var pending = f.RunAsync(Token);
        await stream.Paused.Task.WaitAsync(Token);
        try
        {
            f.Updates.Should().ContainSingle().Which.Answer.Should().Be(text);
            pending.IsCompleted.Should().BeFalse();
            f.Runtime.IsQuiescent.Should().BeFalse();
            f.Reviews.Count.Should().Be(2);
            var posts = f.Http.Requests.Where(item => item.Method is "POST").ToArray();
            posts.Length.Should().Be(2);
            posts[0].Body.Should().Equal(f.Reviews[0].Body);
            posts[1].Body.Should().Equal(f.Reviews[1].Body);
            using var count = JsonDocument.Parse(posts[0].Body);
            using var message = JsonDocument.Parse(posts[1].Body);
            message.RootElement.GetProperty("stream").GetBoolean().Should().BeTrue();
            foreach (var field in new[] { "model", "system", "messages", "thinking" })
            { message.RootElement.GetProperty(field).GetRawText().Should().Be(count.RootElement.GetProperty(field).GetRawText()); }
            message.RootElement.TryGetProperty("tools", out _).Should().BeFalse();
            message.RootElement.GetProperty("messages")[0].GetProperty("content").GetString().Should().Be(f.Context.UserRequest);
        }
        finally { stream.Release.TrySetResult(); }
        var result = await pending;
        result.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        result.Answer.Should().Be(text + " complete");
        f.Updates.Select(update => update.Answer).Should().Equal(text, text + " complete");
        stream.Disposed.Should().BeTrue();
        f.Runtime.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("truncated")]
    [InlineData("duplicate-terminal")]
    [InlineData("after-terminal")]
    [InlineData("max-tokens")]
    [InlineData("error")]
    [InlineData("thinking")]
    [InlineData("tool")]
    [InlineData("citations")]
    [InlineData("wrong-model")]
    [InlineData("wrong-index")]
    [InlineData("duplicate-start")]
    [InlineData("missing-block-stop")]
    [InlineData("missing-message-delta")]
    [InlineData("wrong-name")]
    [InlineData("duplicate-field")]
    [InlineData("unknown-field")]
    [InlineData("unknown-event")]
    [InlineData("bad-utf8")]
    [InlineData("bad-surrogate")]
    [InlineData("depth")]
    [InlineData("input-tokens")]
    [InlineData("output-tokens")]
    [InlineData("cache")]
    [InlineData("buffered")]
    public async Task NativeClaudeRejectsIncompleteUnsupportedAndNonNormalStreamsWithoutPartialSuccess(string fault)
    {
        await using var f = await StreamFixture.CreateAsync();
        var first = StreamStart;
        var delta = StreamText("safe provisional");
        var end = StreamEnd;
        var wire = fault switch
        {
            "truncated" => first + delta,
            "duplicate-terminal" => first + delta + end + Event("message_stop", new { type = "message_stop" }),
            "after-terminal" => first + delta + end + delta,
            "max-tokens" => first + delta + end.Replace("end_turn", "max_tokens", StringComparison.Ordinal),
            "error" => first + delta + Event("error", new { type = "error", error = new { type = "overloaded_error", message = "inert error" } }),
            "thinking" => first.Replace("\"type\":\"text\"", "\"type\":\"thinking\"", StringComparison.Ordinal) + delta + end,
            "tool" => first.Replace("\"type\":\"text\"", "\"type\":\"tool_use\"", StringComparison.Ordinal) + delta + end,
            "citations" => first + delta.Replace("\"type\":\"text_delta\"", "\"type\":\"citations_delta\"", StringComparison.Ordinal) + end,
            "wrong-model" => first.Replace("exact-model", "foreign", StringComparison.Ordinal) + delta + end,
            "wrong-index" => first + delta.Replace("\"index\":0", "\"index\":1", StringComparison.Ordinal) + end,
            "duplicate-start" => first + first + delta + end,
            "missing-block-stop" => first + delta + end[ end.IndexOf("event: message_delta", StringComparison.Ordinal).. ],
            "missing-message-delta" => first + delta + Event("content_block_stop", new { type = "content_block_stop", index = 0 })
                + Event("message_stop", new { type = "message_stop" }),
            "wrong-name" => first + delta.Replace("event: content_block_delta", "event: ping", StringComparison.Ordinal) + end,
            "duplicate-field" => first + delta.Replace("\"index\":0", "\"index\":0,\"index\":0", StringComparison.Ordinal) + end,
            "unknown-field" => first + delta.Replace("\"index\":0", "\"index\":0,\"authority\":true", StringComparison.Ordinal) + end,
            "unknown-event" => first + Event("new_event", new { type = "new_event" }) + delta + end,
            "bad-surrogate" => first + delta.Replace("safe provisional", "\\uD83D", StringComparison.Ordinal) + end,
            "depth" => "event: message_start\ndata: " + new string('[', 25) + "0" + new string(']', 25) + "\n\n",
            "input-tokens" => first.Replace("\"input_tokens\":25", "\"input_tokens\":8193", StringComparison.Ordinal) + delta + end,
            "output-tokens" => first + delta + end.Replace("\"output_tokens\":5", "\"output_tokens\":4097", StringComparison.Ordinal),
            "cache" => first.Replace("\"input_tokens\":25", "\"cache_read_input_tokens\":1,\"input_tokens\":25", StringComparison.Ordinal) + delta + end,
            "buffered" => Answer,
            _ => first + delta + end,
        };
        f.Respond(wire, 1);
        if (fault is "bad-utf8") { f.Stream!.Bytes[10] = 0xff; }
        var result = await f.RunAsync(Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        result.Answer.Should().BeNull();
        f.Updates.Should().OnlyContain(update => string.Equals(update.Answer, "safe provisional", StringComparison.Ordinal));
        f.Stream!.Disposed.Should().BeTrue();
        f.Runtime.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public async Task NativeClaudeStreamDenialIsPerPhysicalSendWithoutRetry(int approvals, int posts)
    {
        await using var f = await StreamFixture.CreateAsync();
        f.Approvals = approvals;
        f.Respond(StreamStart + StreamText("answer") + StreamEnd);
        var result = await f.RunAsync(Token);
        result.Outcome.Should().Be(approvals == 2 ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.DeniedEgress);
        f.Http.Requests.Count(item => item.Method is "POST").Should().Be(posts);
        if (approvals < 2) { f.Updates.Should().BeEmpty(); result.Answer.Should().BeNull(); }
    }

    [Theory]
    [InlineData("unknown-mode")]
    [InlineData("different-model")]
    [InlineData("different-reasoning")]
    [InlineData("different-context")]
    [InlineData("different-limits")]
    public async Task NativeClaudeStreamNeedsPositiveExactProfileEvidenceBeforeAnyContentPost(string fault)
    {
        var evidence = fault switch
        {
            "different-model" => new ClaudeTextStreamProfile(new("other"), new("disabled"), new("8192"), 8192, 4096),
            "different-reasoning" => new(new("exact-model"), new("high"), new("8192"), 8192, 4096),
            "different-context" => new(new("exact-model"), new("disabled"), new("4096"), 8192, 4096),
            _ => new(new("exact-model"), new("disabled"), new("8192"), 8192, 4095),
        };
        await using var f = await StreamFixture.CreateAsync(fault is "unknown-mode" ? [] : [evidence]);
        var result = await f.RunAsync(Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        f.Http.Requests.Should().OnlyContain(item => string.Equals(item.Method, "GET", StringComparison.Ordinal));
        f.Updates.Should().BeEmpty();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("account")]
    [InlineData("approval")]
    [InlineData("expiry")]
    [InlineData("cancel")]
    public async Task NativeClaudeStreamRevalidatesEachPublicationAndJoinsCancellationIgnoringReads(string fault)
    {
        await using var f = await StreamFixture.CreateAsync();
        var first = StreamStart + StreamText("first");
        var stream = f.Respond(first + StreamText(" forbidden late") + StreamEnd, 1, Encoding.UTF8.GetByteCount(first));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var pending = f.RunAsync(cancellation.Token);
        await stream.Paused.Task.WaitAsync(Token);
        try
        {
            switch (fault)
            {
                case "source": f.Live = false; break;
                case "account": f.Accounts.Revoke(); break;
                case "approval": f.Http.Approved = false; break;
                case "expiry": f.Time.Now = f.Context.ExpiresAt; break;
                case "cancel": await cancellation.CancelAsync(); break;
            }
            f.Runtime.IsQuiescent.Should().BeFalse();
            pending.IsCompleted.Should().BeFalse();
            f.Updates.Should().ContainSingle().Which.Answer.Should().Be("first");
        }
        finally { stream.Release.TrySetResult(); }
        var result = await pending;
        result.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        result.Answer.Should().BeNull();
        f.Updates.Should().ContainSingle();
        stream.Disposed.Should().BeTrue();
        f.Runtime.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task NativeClaudeFailedReaderDisposalQuarantinesRuntimeAndCannotReportQuiescenceOrReuse()
    {
        var f = await StreamFixture.CreateAsync();
        try
        {
            var stream = f.Respond(StreamStart + StreamText("first") + StreamEnd);
            stream.FailDisposal = true;
            var result = await f.RunAsync(Token);
            result.Outcome.Should().Be(ModelTurnOutcome.Unknown);
            result.Answer.Should().BeNull();
            f.Runtime.IsQuiescent.Should().BeFalse();
            var posts = f.Http.Requests.Count(item => item.Method is "POST");
            (await f.RunAsync(Token)).Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
            f.Http.Requests.Count(item => item.Method is "POST").Should().Be(posts);
            await Assert.ThrowsAsync<IOException>(() => f.Runtime.DisposeAsync().AsTask());
        }
        finally { await f.Accounts.DisposeAsync(); }
    }

    [Theory]
    [InlineData(-1, ModelTurnOutcome.Succeeded)]
    [InlineData(0, ModelTurnOutcome.Succeeded)]
    [InlineData(1, ModelTurnOutcome.Unknown)]
    public async Task NativeClaudeCumulativeOutputByteBudgetIsInclusive(int difference, ModelTurnOutcome expected)
    {
        await using var f = await StreamFixture.CreateAsync();
        var wire = StreamStart + StreamText("answer") + StreamEnd;
        var budget = ModelContextEnvelope.MaximumOutputUtf8Bytes - Encoding.UTF8.GetByteCount("{\"input_tokens\":25}");
        wire += new string('\n', budget - Encoding.UTF8.GetByteCount(wire) + difference);
        f.Respond(wire);
        (await f.RunAsync(Token)).Outcome.Should().Be(expected);
    }

    [Theory]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public async Task NativeClaudeEventBudgetIsCumulativeAndInclusive(int events, bool valid)
    {
        var pings = string.Concat(Enumerable.Repeat(Event("ping", new { type = "ping" }), events - 6));
        var wire = StreamStart + pings + StreamText("answer") + StreamEnd;
        var content = new StringContent(wire, Encoding.UTF8, "text/event-stream");
        using (content)
        {
            var profile = new Kora.Core.Providers.ProviderProfileTuple(Kora.Core.Providers.ProviderService.Claude,
                new("account"), new(1), new("exact-model"), new("disabled"), new("8192"));
            Task<byte[]> Read() => ClaudeAnswerStream.ReadAsync(content, profile, 8192, 4096, 16384,
                _ => ValueTask.CompletedTask, () => throw new InvalidOperationException("No failed disposal expected."), Token);
            if (valid) { ClaudeMessagesRuntime.ParseAnswer(await Read(), profile, 4096, 8192).Should().Be("answer"); }
            else { await Assert.ThrowsAsync<InvalidDataException>(Read); }
        }
    }

    [Theory]
    [InlineData(8192, 4096, true)]
    [InlineData(8193, 4096, false)]
    [InlineData(8192, 4097, false)]
    public async Task NativeClaudeTokenCeilingsAreExactAndInclusive(int input, int output, bool valid)
    {
        await using var f = await StreamFixture.CreateAsync();
        var wire = StreamStart.Replace("\"input_tokens\":25", "\"input_tokens\":" + input.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal) + StreamText("answer")
            + StreamEnd.Replace("\"output_tokens\":5", "\"output_tokens\":" + output.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        f.Respond(wire);
        (await f.RunAsync(Token)).Outcome.Should().Be(valid ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.Unknown);
    }

    [Fact]
    public async Task NativeClaudeFailedStopStillJoinsPhysicalReaderAndQuarantinesTheOwner()
    {
        var f = await StreamFixture.CreateAsync();
        var first = StreamStart + StreamText("preview");
        var stream = f.Respond(first + StreamEnd, 1, Encoding.UTF8.GetByteCount(first));
        stream.FailStop = true;
        var pending = f.RunAsync(Token);
        await stream.Paused.Task.WaitAsync(Token);
        var disposal = f.Runtime.DisposeAsync().AsTask();
        try
        {
            f.Runtime.IsQuiescent.Should().BeFalse();
            disposal.IsCompleted.Should().BeFalse();
            pending.IsCompleted.Should().BeFalse();
        }
        finally { stream.Release.TrySetResult(); }
        try
        {
            (await pending).Outcome.Should().Be(ModelTurnOutcome.Cancelled);
            await Assert.ThrowsAnyAsync<Exception>(() => disposal);
            f.Runtime.IsQuiescent.Should().BeFalse();
        }
        finally { await f.Accounts.DisposeAsync(); }
    }

    internal sealed class StreamFixture : IAsyncDisposable
    {
        internal MutableStreamTime Time { get; } = new();
        internal ClaudeAccountBrokerTests.Fixture Http { get; }
        internal ClaudeAccountBroker Accounts { get; }
        internal ClaudeMessagesRuntime Runtime { get; }
        internal Kora.Core.Providers.ProviderProfileTuple Profile { get; private set; } = null!;
        internal ModelTurnProvenance Provenance { get; private set; } = null!;
        internal ModelContextEnvelope Context { get; private set; } = null!;
        internal List<ModelAnswerUpdate> Updates { get; } = [];
        internal List<ClaudeTransmission> Reviews { get; } = [];
        internal bool Live { get; set; } = true;
        internal int Approvals { get; set; } = 2;
        internal ProgressiveStream? Stream { get; private set; }
        private StreamFixture(IEnumerable<ClaudeTextStreamProfile> evidence)
        {
            Http = new() { Time = Time, Approval = new(DateTimeOffset.UtcNow.AddHours(1), evidence) };
            Accounts = Http.Create();
            Runtime = new(Accounts, Time, NullLogger<ClaudeMessagesRuntime>.Instance, Http.CreateHttp);
        }
        internal static async Task<StreamFixture> CreateAsync(IEnumerable<ClaudeTextStreamProfile>? evidence = null)
        {
            var f = new StreamFixture(evidence ?? [new(new("exact-model"), new("disabled"), new("8192"), 8192, 4096)]);
            using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
            await f.Accounts.SignInAsync(ClaudeAccountBrokerTests.Fixture.Credential, () => true, Token);
            var catalogue = await f.Accounts.ObserveAsync(Token);
            f.Profile = f.Http.Profile(catalogue);
            f.Provenance = new(new(Guid.NewGuid()), root.Request, catalogue.Models[0].Selection);
            f.Context = new(root.Request, "policy", "  original\r\nuser  ", [], f.Time.Now, f.Time.Now.AddMinutes(1), ModelToolMode.None);
            return f;
        }
        internal ProgressiveStream Respond(string wire, int segment = 4096, int pauseAt = -1)
        {
            Stream = new(Encoding.UTF8.GetBytes(wire), segment, pauseAt);
            Http.Respond = request => request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
                ? ClaudeAccountBrokerTests.Fixture.Json("{\"input_tokens\":25}") : StreamingResponse(Stream);
            return Stream;
        }
        internal Task<ClaudeTurnReply> RunAsync(CancellationToken token) =>
            Runtime.RunWithAnswerAsync(Provenance, Profile, Context, (transmission, _) =>
            {
                Http.Requests.Count(item => item.Method is "POST").Should().Be(Reviews.Count);
                Reviews.Add(transmission);
                return ValueTask.FromResult(Reviews.Count <= Approvals);
            }, _ => ValueTask.FromResult(Live), (update, _) => { Updates.Add(update); return ValueTask.CompletedTask; },
                token);
        public async ValueTask DisposeAsync() { await Runtime.DisposeAsync(); await Accounts.DisposeAsync(); }
    }

    internal static HttpResponseMessage StreamingResponse(Stream stream)
    {
        var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        return new(HttpStatusCode.OK) { Content = content };
    }

    internal sealed class ProgressiveStream(byte[] bytes, int segment, int pauseAt = -1) : Stream
    {
        private int position;
        private CancellationTokenRegistration stopRegistration;
        private bool registered;
        internal byte[] Bytes => bytes;
        internal TaskCompletionSource Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed { get; private set; }
        internal bool FailDisposal { get; set; }
        internal bool FailStop { get; set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (FailStop && !registered)
            {
                registered = true;
                stopRegistration = cancellationToken.Register(() => throw new IOException("Synthetic stop failure."));
            }
            if (position == pauseAt)
            {
                Paused.TrySetResult();
                await Release.Task.ConfigureAwait(false);
            }
            var count = Math.Min(Math.Min(segment, buffer.Length), bytes.Length - position);
            if (position < pauseAt) { count = Math.Min(count, pauseAt - position); }
            bytes.AsMemory(position, count).CopyTo(buffer);
            position += count;
            return count;
        }
        public override async ValueTask DisposeAsync()
        {
            Disposed = true;
            await stopRegistration.DisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
            if (FailDisposal) { throw new IOException("Synthetic disposal failure."); }
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    internal sealed class MutableStreamTime : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
