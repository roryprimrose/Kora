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
    [InlineData(false)]
    [InlineData(true)]
    public async Task Buffered_turn_bounds_both_physical_request_bodies_before_any_disclosure(bool overflow)
    {
        var f = new ClaudeAccountBrokerTests.Fixture();
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await broker.SignInAsync(ClaudeAccountBrokerTests.Fixture.Credential, () => true, Token);
        var catalogue = await broker.ObserveAsync(Token);
        var profile = f.Profile(catalogue);
        var now = f.Time.GetUtcNow();
        ModelContextEnvelope Context(string body) => new(root.Request, "policy", body, [], now, now.AddMinutes(1), ModelToolMode.None);
        var original = Context("body");
        var originalBytes = ClaudeMessagesRuntime.Serialize(original, profile, null).Length
            + ClaudeMessagesRuntime.Serialize(original, profile, 4096).Length;
        var body = "body" + new string('x', (ModelContextEnvelope.MaximumInputUtf8Bytes - originalBytes) / 2 + (overflow ? 1 : 0));
        var context = Context(body);
        context.RequireCompleteInputLimit();
        var physicalBytes = ClaudeMessagesRuntime.Serialize(context, profile, null).Length
            + ClaudeMessagesRuntime.Serialize(context, profile, 4096).Length;
        if (overflow) { physicalBytes.Should().BeGreaterThan(ModelContextEnvelope.MaximumInputUtf8Bytes); }
        else { physicalBytes.Should().BeInRange(ModelContextEnvelope.MaximumInputUtf8Bytes - 1, ModelContextEnvelope.MaximumInputUtf8Bytes); }
        f.Respond = message => ClaudeAccountBrokerTests.Fixture.Json(message.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
            ? "{\"input_tokens\":25}" : Answer);
        await using var runtime = new ClaudeMessagesRuntime(broker, f.Time, NullLogger<ClaudeMessagesRuntime>.Instance, f.CreateHttp);
        var reviews = new List<ClaudeTransmission>();
        var result = await runtime.RunAsync(new(new(Guid.NewGuid()), root.Request, catalogue.Models[0].Selection),
            profile, context, (review, _) => { reviews.Add(review); return ValueTask.FromResult(true); },
            _ => ValueTask.FromResult(true), Token);
        result.Outcome.Should().Be(overflow ? ModelTurnOutcome.DeniedEgress : ModelTurnOutcome.Succeeded);
        result.Answer.Should().Be(overflow ? null : "genuine exact answer");
        reviews.Count.Should().Be(overflow ? 0 : 2);
        f.Requests.Count(request => request.Method is "POST").Should().Be(reviews.Count);
        runtime.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task Buffered_turn_shares_response_budget_between_count_and_answer(int excess, bool declaredLength)
    {
        const string count = "{\"input_tokens\":25}";
        var responseBytes = ModelContextEnvelope.MaximumOutputUtf8Bytes - Encoding.UTF8.GetByteCount(count) + excess;
        var response = Answer.PadRight(responseBytes);
        await AssertBufferedResponseBudgetAsync(count, response, declaredLength,
            excess > 0 ? ModelTurnOutcome.Unknown : ModelTurnOutcome.Succeeded, 2);
    }

    [Theory]
    [InlineData(-1, false, 2, ModelTurnOutcome.Unknown)]
    [InlineData(0, false, 1, ModelTurnOutcome.DeniedEgress)]
    [InlineData(1, false, 1, ModelTurnOutcome.Unknown)]
    [InlineData(-1, true, 2, ModelTurnOutcome.Unknown)]
    [InlineData(0, true, 1, ModelTurnOutcome.DeniedEgress)]
    [InlineData(1, true, 1, ModelTurnOutcome.Unknown)]
    public async Task Exhausted_count_budget_cannot_authorize_another_post(int excess, bool declaredLength, int posts, ModelTurnOutcome outcome)
    {
        var count = "{\"input_tokens\":25}".PadRight(ModelContextEnvelope.MaximumOutputUtf8Bytes + excess);
        await AssertBufferedResponseBudgetAsync(count, Answer, declaredLength, outcome, posts);
    }

    private static async Task AssertBufferedResponseBudgetAsync(string count, string answer, bool declaredLength,
        ModelTurnOutcome outcome, int posts)
    {
        var f = new ClaudeAccountBrokerTests.Fixture();
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await broker.SignInAsync(ClaudeAccountBrokerTests.Fixture.Credential, () => true, Token);
        var catalogue = await broker.ObserveAsync(Token);
        var profile = f.Profile(catalogue);
        var now = f.Time.GetUtcNow();
        var context = new ModelContextEnvelope(root.Request, "policy", "original body", [], now, now.AddMinutes(1), ModelToolMode.None);
        var streams = new List<BudgetReadStream>();
        f.Respond = message =>
        {
            var wire = message.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal) ? count : answer;
            var stream = new BudgetReadStream(Encoding.UTF8.GetBytes(wire));
            streams.Add(stream);
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StreamContent(stream) };
            response.Content.Headers.ContentType = new("application/json");
            if (declaredLength) { response.Content.Headers.ContentLength = Encoding.UTF8.GetByteCount(wire); }
            return response;
        };
        await using var runtime = new ClaudeMessagesRuntime(broker, f.Time, NullLogger<ClaudeMessagesRuntime>.Instance, f.CreateHttp);
        var reviews = new List<ClaudeTransmission>();
        var result = await runtime.RunAsync(new(new(Guid.NewGuid()), root.Request, catalogue.Models[0].Selection),
            profile, context, (review, _) => { reviews.Add(review); return ValueTask.FromResult(true); },
            _ => ValueTask.FromResult(true), Token);
        result.Outcome.Should().Be(outcome);
        result.Answer.Should().Be(outcome == ModelTurnOutcome.Succeeded ? "genuine exact answer" : null);
        reviews.Count.Should().Be(posts);
        f.Requests.Count(request => request.Method is "POST").Should().Be(posts);
        streams.Sum(stream => stream.BytesRead).Should().BeLessThanOrEqualTo(ModelContextEnvelope.MaximumOutputUtf8Bytes + 1);
        streams.Should().OnlyContain(stream => stream.Disposed);
        runtime.IsQuiescent.Should().BeTrue();
    }

    private sealed class BudgetReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        internal int BytesRead { get; private set; }
        internal bool Disposed { get; private set; }
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            return read;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
