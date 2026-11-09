using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Windows.Dependencies;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class WindowsOllamaToolTransportTests
{
    [Theory]
    [InlineData("version", "stream", false)]
    [InlineData("tags", "response", false)]
    [InlineData("chat", "stream", false)]
    [InlineData("chat", "response", false)]
    [InlineData("version", "response", true)]
    [InlineData("tags", "stream", true)]
    [InlineData("chat", "stream", true)]
    [InlineData("chat", "response", true)]
    public async Task FailedNativeReleaseRejectsSuccessAndReuseAndRemainsAnExplicitDisposalFailure(
        string stage, string resource, bool streaming)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        var handler = new NativeDisposalHandler(stage, resource, streaming);
        var transport = DisposalTransport(f, handler);
        var updates = new List<string>();
        var request = f.Request(toolMode: ModelToolMode.None);
        var reply = streaming
            ? await transport.CompleteWithAnswerAsync(request, (update, _) =>
            {
                updates.Add(update.Answer);
                return ValueTask.CompletedTask;
            }, Token)
            : await transport.CompleteAsync(request, Token);
        reply.Message.Should().BeNull();
        reply.Usage.Should().BeNull();
        updates.Should().OnlyContain(answer => answer == "provisional");
        var sends = handler.Sends;
        (await transport.CompleteAsync(request, Token)).Outcome.Should().Be(ModelTurnOutcome.Denied);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), f.RequestIdentity, f.Provenance.Selection);
        var next = ModelToolProtocol.CreateRequest(provenance, f.Profile,
            ModelToolProtocol.ReadContext(request.Context.Wire.AsSpan(), provenance), [], 1);
        (await transport.CompleteAsync(next, Token)).Outcome.Should().Be(ModelTurnOutcome.Denied);
        handler.Sends.Should().Be(sends);
        var firstFailure = await Assert.ThrowsAsync<IOException>(() => transport.DisposeAsync().AsTask());
        var repeatedFailure = await Assert.ThrowsAsync<IOException>(() => transport.DisposeAsync().AsTask());
        repeatedFailure.Should().BeSameAs(firstFailure);
        firstFailure.InnerException.Should().BeOfType<IOException>().Which.Message
            .Should().Be($"Synthetic native {resource} release failure.");
        reply.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        handler.DisposeAttempts.Should().Be(1);
        handler.Responses.Should().OnlyContain(response => response.DisposeAttempts == 1
            && response.Reader.AsyncDisposeAttempts == 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeReleaseFailureCannotBecomeSuccessfulTransportDisposal(bool streaming)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        var handler = new NativeDisposalHandler("chat", "stream", streaming);
        var transport = DisposalTransport(f, handler);
        var request = f.Request(toolMode: ModelToolMode.None);
        var reply = streaming
            ? await transport.CompleteWithAnswerAsync(request, static (_, _) => ValueTask.CompletedTask, Token)
            : await transport.CompleteAsync(request, Token);
        reply.Message.Should().BeNull();
        await Assert.ThrowsAsync<IOException>(() => transport.DisposeAsync().AsTask());
        await Assert.ThrowsAsync<IOException>(() => transport.DisposeAsync().AsTask());
        handler.DisposeAttempts.Should().Be(1);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NativeDisposalJoinsPendingReleaseAndDoesNotBlockIndependentTransports(bool streaming, bool fail)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        var handler = new NativeDisposalHandler("chat", fail ? "stream" : "none", streaming, pauseDisposal: true);
        var transport = DisposalTransport(f, handler);
        var request = f.Request(toolMode: ModelToolMode.None);
        var updates = 0;
        var pending = streaming
            ? transport.CompleteWithAnswerAsync(request, (_, _) =>
            {
                updates++;
                return ValueTask.CompletedTask;
            }, Token)
            : transport.CompleteAsync(request, Token);
        await handler.DisposalEntered.Task.WaitAsync(Token);
        var disposal = transport.DisposeAsync().AsTask();
        try
        {
            pending.IsCompleted.Should().BeFalse();
            disposal.IsCompleted.Should().BeFalse();
            handler.DisposeAttempts.Should().Be(0);
            (await transport.CompleteAsync(request, Token)).Outcome.Should().Be(ModelTurnOutcome.Denied);
            handler.Sends.Should().Be(3);
            (await f.Transport.CompleteAsync(request, Token)).Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        }
        finally { handler.ReleaseDisposal.TrySetResult(); }
        var reply = await pending;
        reply.Outcome.Should().Be(fail ? ModelTurnOutcome.Unknown : ModelTurnOutcome.Cancelled);
        reply.Message.Should().BeNull();
        updates.Should().Be(streaming ? 1 : 0);
        if (fail)
        {
            await Assert.ThrowsAsync<IOException>(() => disposal);
            await Assert.ThrowsAsync<IOException>(() => transport.DisposeAsync().AsTask());
        }
        else
        {
            await disposal;
            await transport.DisposeAsync();
        }
        handler.DisposeAttempts.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeHandlerReleaseFailureIsPersistentAndNeverRetried(bool streaming)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        var handler = new NativeDisposalHandler("client", "handler", streaming);
        var transport = DisposalTransport(f, handler);
        var request = f.Request(toolMode: ModelToolMode.None);
        var reply = streaming
            ? await transport.CompleteWithAnswerAsync(request, static (_, _) => ValueTask.CompletedTask, Token)
            : await transport.CompleteAsync(request, Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        await Assert.ThrowsAsync<IOException>(() => transport.DisposeAsync().AsTask());
        await Assert.ThrowsAsync<IOException>(() => transport.DisposeAsync().AsTask());
        (await transport.CompleteAsync(request, Token)).Outcome.Should().Be(ModelTurnOutcome.Denied);
        handler.DisposeAttempts.Should().Be(1);
        handler.Sends.Should().Be(3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeSuccessfulReleaseAllowsFreshAdmittedTurnsAndIdempotentDisposal(bool streaming)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        var handler = new NativeDisposalHandler("none", "none", streaming);
        await using var transport = DisposalTransport(f, handler);
        var request = f.Request(toolMode: ModelToolMode.None);
        for (var turn = 0; turn < 2; turn++)
        {
            var reply = streaming
                ? await transport.CompleteWithAnswerAsync(request, static (_, _) => ValueTask.CompletedTask, Token)
                : await transport.CompleteAsync(request, Token);
            reply.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
            reply.Message.Should().BeOfType<ModelFinalResponse>().Which.Answer
                .Should().Be(streaming ? "provisional" : "Genuine answer");
            var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), f.RequestIdentity, f.Provenance.Selection);
            request = ModelToolProtocol.CreateRequest(provenance, f.Profile,
                ModelToolProtocol.ReadContext(request.Context.Wire.AsSpan(), provenance), [], 1);
        }
        await transport.DisposeAsync();
        await transport.DisposeAsync();
        handler.Sends.Should().Be(6);
        handler.DisposeAttempts.Should().Be(1);
        handler.Responses.Should().OnlyContain(response => response.DisposeAttempts == 1
            && response.Reader.AsyncDisposeAttempts == 1);
    }

    private static WindowsOllamaToolTransport DisposalTransport(Fixture f, HttpMessageHandler handler) =>
        new(new HttpClient(handler), request => new(request.Provenance, f.Profile, Model, Digest, "fake-runtime",
            4242, f.Time.GetUtcNow(), @"C:\fake\ollama.exe", Digest, f.Time.GetUtcNow().AddMinutes(1),
            () => true, f.Revoked.Token), static (_, _) => Task.FromResult(true),
            f.Time, NullLogger<WindowsOllamaToolTransport>.Instance);

    private sealed class NativeDisposalHandler(string stage, string resource, bool streaming, bool pauseDisposal = false)
        : HttpMessageHandler
    {
        internal int Sends { get; private set; }
        internal int DisposeAttempts { get; private set; }
        internal List<NativeDisposalResponse> Responses { get; } = [];
        internal TaskCompletionSource DisposalEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseDisposal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sends++;
            var current = request.RequestUri!.AbsolutePath switch
            {
                "/api/version" => "version",
                "/api/tags" => "tags",
                _ => "chat",
            };
            var body = current switch
            {
                "version" => """{"version":"fake-runtime"}""",
                "tags" => JsonSerializer.Serialize(new { models = new[] { new { name = Model, digest = Digest } } }),
                _ => streaming ? AnswerChunk("provisional") + "\n" + AnswerTerminal + "\n" : Final,
            };
            var response = new NativeDisposalResponse(request, body,
                string.Equals(stage, current, StringComparison.Ordinal) ? resource : "none",
                pauseDisposal && string.Equals(stage, current, StringComparison.Ordinal) ? DisposalEntered : null,
                pauseDisposal && string.Equals(stage, current, StringComparison.Ordinal) ? ReleaseDisposal : null);
            if (streaming && current is "chat")
            { response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson"); }
            Responses.Add(response);
            return Task.FromResult<HttpResponseMessage>(response);
        }

        protected override void Dispose(bool disposing)
        {
            DisposeAttempts++;
            base.Dispose(disposing);
            if (resource is "handler") { throw new IOException("Synthetic native handler release failure."); }
        }
    }

    private sealed class NativeDisposalResponse : HttpResponseMessage
    {
        private readonly bool fail;
        internal NativeDisposalReader Reader { get; }
        internal int DisposeAttempts { get; private set; }

        internal NativeDisposalResponse(HttpRequestMessage request, string body, string resource,
            TaskCompletionSource? entered, TaskCompletionSource? release) : base(HttpStatusCode.OK)
        {
            fail = resource is "response";
            RequestMessage = request;
            Reader = new(body, resource is "stream", entered, release);
            Content = new StreamContent(Reader);
            Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        protected override void Dispose(bool disposing)
        {
            DisposeAttempts++;
            base.Dispose(disposing);
            if (fail) { throw new IOException("Synthetic native response release failure."); }
        }
    }

    private sealed class NativeDisposalReader(string body, bool fail,
        TaskCompletionSource? entered, TaskCompletionSource? release) : MemoryStream(Encoding.UTF8.GetBytes(body))
    {
        internal int AsyncDisposeAttempts { get; private set; }

        public override async ValueTask DisposeAsync()
        {
            AsyncDisposeAttempts++;
            entered?.TrySetResult();
            if (release is not null) { await release.Task; }
            await base.DisposeAsync();
            if (fail) { throw new IOException("Synthetic native stream release failure."); }
        }
    }
}
