using System.Net;
using System.Text;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class ClaudeMessagesRuntimeTests
{
    [Theory]
    [InlineData("count-response", false)]
    [InlineData("message-response", false)]
    [InlineData("count-reader", false)]
    [InlineData("message-reader", false)]
    [InlineData("client", false)]
    [InlineData("none", false)]
    [InlineData("message-response", true)]
    [InlineData("message-reader", true)]
    [InlineData("client", true)]
    [InlineData("none", true)]
    public async Task Buffered_physical_disposal_must_confirm_release_before_answer_or_reuse(string fault, bool atResponseBudget)
    {
        var f = new ClaudeAccountBrokerTests.Fixture();
        await using var broker = f.Create();
        using (var setup = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request))
        { await broker.SignInAsync(ClaudeAccountBrokerTests.Fixture.Credential, () => true, Token); }
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice), HostActivityLayer.Windows, HostOperation.Request);
        var catalogue = await broker.ObserveAsync(Token);
        var profile = f.Profile(catalogue);
        var now = f.Time.GetUtcNow();
        var context = new ModelContextEnvelope(root.Request, "policy", "  original\r\nbody  ", [],
            now, now.AddMinutes(1), ModelToolMode.None);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), root.Request, catalogue.Models[0].Selection);
        var handler = new BufferedDisposalHandler(fault, atResponseBudget: atResponseBudget);
        var clients = 0;
        var runtime = new ClaudeMessagesRuntime(broker, f.Time, NullLogger<ClaudeMessagesRuntime>.Instance,
            () => { clients++; return new(handler); });
        var reviews = new List<ClaudeTransmission>();
        Task<ClaudeTurnReply> Run() => runtime.RunAsync(provenance, profile, context, (review, _) =>
        {
            review.Provenance.Should().BeSameAs(provenance);
            review.Profile.Should().Be(profile);
            Encoding.UTF8.GetString(review.Body.AsSpan()).Should().Contain("original\\r\\nbody");
            review.Headers.Should().NotContainKey("x-api-key");
            reviews.Add(review);
            return ValueTask.FromResult(true);
        }, _ => ValueTask.FromResult(true), Token);
        try
        {
            var result = await Run();
            result.Outcome.Should().Be(fault is "none" ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.Unknown);
            result.Answer.Should().Be(fault is "none" ? "genuine exact answer" : null);
            handler.Sends.Should().Be(fault.StartsWith("count-", StringComparison.Ordinal) ? 1 : 2);
            reviews.Should().HaveCount(handler.Sends);
            clients.Should().Be(1);
            handler.DisposeAttempts.Should().Be(1);
            handler.Responses.Should().OnlyContain(response => response.DisposeAttempts == 1
                && response.Reader.AsyncDisposeAttempts == 1 && response.Reader.SyncDisposeAttempts >= 1);
            runtime.IsQuiescent.Should().Be(fault is "none");
            if (fault is not "none")
            {
                var sends = handler.Sends;
                (await Run()).Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
                handler.Sends.Should().Be(sends);
                reviews.Should().HaveCount(sends);
                clients.Should().Be(1);
            }
        }
        finally
        {
            if (runtime.IsQuiescent) { await runtime.DisposeAsync(); }
            else
            {
                await Assert.ThrowsAsync<IOException>(() => runtime.DisposeAsync().AsTask());
                await Assert.ThrowsAsync<IOException>(() => runtime.DisposeAsync().AsTask());
                runtime.IsQuiescent.Should().BeFalse();
            }
        }
    }

    [Fact]
    public async Task Buffered_physical_disposal_is_joined_before_shutdown_and_failure_retains_ownership()
    {
        var f = new ClaudeAccountBrokerTests.Fixture();
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await broker.SignInAsync(ClaudeAccountBrokerTests.Fixture.Credential, () => true, Token);
        var catalogue = await broker.ObserveAsync(Token);
        var now = f.Time.GetUtcNow();
        var context = new ModelContextEnvelope(root.Request, "policy", "original", [], now, now.AddMinutes(1), ModelToolMode.None);
        var handler = new BufferedDisposalHandler("count-reader", pauseDisposal: true);
        var runtime = new ClaudeMessagesRuntime(broker, f.Time, NullLogger<ClaudeMessagesRuntime>.Instance,
            () => new(handler));
        var run = runtime.RunAsync(new(new(Guid.NewGuid()), root.Request, catalogue.Models[0].Selection),
            f.Profile(catalogue), context, (_, _) => ValueTask.FromResult(true), _ => ValueTask.FromResult(true), Token);
        await handler.DisposalEntered.Task.WaitAsync(Token);
        var disposal = runtime.DisposeAsync().AsTask();
        try
        {
            runtime.IsQuiescent.Should().BeFalse();
            run.IsCompleted.Should().BeFalse();
            disposal.IsCompleted.Should().BeFalse();
            handler.DisposeAttempts.Should().Be(0);
        }
        finally { handler.ReleaseDisposal.TrySetResult(); }
        var result = await run;
        result.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        result.Answer.Should().BeNull();
        await Assert.ThrowsAsync<IOException>(() => disposal);
        runtime.IsQuiescent.Should().BeFalse();
        handler.Sends.Should().Be(1);
        handler.DisposeAttempts.Should().Be(1);
        handler.Responses.Single().DisposeAttempts.Should().Be(1);
    }

    private sealed class BufferedDisposalHandler(string fault, bool pauseDisposal = false, bool atResponseBudget = false) : HttpMessageHandler
    {
        internal int Sends { get; private set; }
        internal int DisposeAttempts { get; private set; }
        internal List<BufferedDisposalResponse> Responses { get; } = [];
        internal TaskCompletionSource DisposalEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseDisposal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var count = request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal);
            Sends++;
            var prefix = count ? "count-" : "message-";
            const string countBody = "{\"input_tokens\":25}";
            var answer = atResponseBudget
                ? Answer.PadRight(ModelContextEnvelope.MaximumOutputUtf8Bytes - Encoding.UTF8.GetByteCount(countBody))
                : Answer;
            if (atResponseBudget)
            { (Encoding.UTF8.GetByteCount(countBody) + Encoding.UTF8.GetByteCount(answer)).Should().Be(ModelContextEnvelope.MaximumOutputUtf8Bytes); }
            var response = new BufferedDisposalResponse(count ? countBody : answer,
                string.Equals(fault, prefix + "response", StringComparison.Ordinal),
                string.Equals(fault, prefix + "reader", StringComparison.Ordinal),
                pauseDisposal ? DisposalEntered : null, pauseDisposal ? ReleaseDisposal : null);
            Responses.Add(response);
            return Task.FromResult<HttpResponseMessage>(response);
        }
        protected override void Dispose(bool disposing)
        {
            DisposeAttempts++;
            base.Dispose(disposing);
            if (fault is "client") { throw new IOException("Synthetic buffered client disposal failure."); }
        }
    }

    internal sealed class BufferedDisposalResponse : HttpResponseMessage
    {
        private readonly bool fail;
        internal BufferedDisposalReader Reader { get; }
        internal int DisposeAttempts { get; private set; }
        internal BufferedDisposalResponse(string body, bool failResponse, bool failReader = false,
            TaskCompletionSource? entered = null, TaskCompletionSource? release = null) : base(HttpStatusCode.OK)
        {
            fail = failResponse;
            Reader = new(body, failReader, entered, release);
            Content = new StreamContent(Reader);
            Content.Headers.ContentType = new("application/json");
        }
        protected override void Dispose(bool disposing)
        {
            DisposeAttempts++;
            base.Dispose(disposing);
            if (fail) { throw new IOException("Synthetic buffered response disposal failure."); }
        }
    }

    internal sealed class BufferedDisposalReader(string body, bool fail, TaskCompletionSource? entered,
        TaskCompletionSource? release) : MemoryStream(Encoding.UTF8.GetBytes(body))
    {
        internal int AsyncDisposeAttempts { get; private set; }
        internal int SyncDisposeAttempts { get; private set; }
        public override async ValueTask DisposeAsync()
        {
            AsyncDisposeAttempts++;
            entered?.TrySetResult();
            if (release is not null) { await release.Task.ConfigureAwait(false); }
            await base.DisposeAsync().ConfigureAwait(false);
            if (fail) { throw new IOException("Synthetic buffered reader disposal failure."); }
        }
        protected override void Dispose(bool disposing)
        {
            SyncDisposeAttempts++;
            base.Dispose(disposing);
        }
    }
}
