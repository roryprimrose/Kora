using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Application.UnitTests.Dependencies;

[Collection("Host tracing")]
public sealed class ClaudeModelTurnAdapterTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public ClaudeModelTurnAdapterTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("unbound")]
    [InlineData("profile")]
    [InlineData("request")]
    [InlineData("bytes")]
    [InlineData("expired")]
    [InlineData("current")]
    public async Task Nonexact_or_stale_admission_never_enters_Claude_runtime(string fault)
    {
        var f = new Fixture();
        using var root = f.Root();
        var authorization = f.Reviews.PrepareClaudeTurn(f.Context, f.Profile);
        var runtime = new Runtime();
        var adapter = fault is "unbound" ? new ClaudeModelTurnAdapter(runtime, f.Reviews)
            : new ClaudeModelTurnAdapter(runtime, f.Reviews).Bind(authorization, _ => ValueTask.FromResult(fault is not "current"));
        if (fault is "expired") { f.Time.Now = f.Context.ExpiresAt; }
        var provenance = f.Provenance with { Request = fault is "request" ? HostRequest.Create(RequestOrigin.LocalUi) : f.Request };
        var result = await adapter.RunAsync(provenance, fault is "profile" ? f.Profile with { Account = new("foreign") } : f.Profile,
            fault is "bytes" ? new byte[] { 1 } : f.Context.Serialize(),
            (_, _, _) => throw new InvalidOperationException("No Claude tool callback exists."), Token);
        result.Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
        result.Response.Should().BeNull(); runtime.Calls.Should().Be(0);
        adapter.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("success")]
    [InlineData("wrong-transmission")]
    [InlineData("wrong-profile")]
    [InlineData("late-answer")]
    [InlineData("runtime-denied")]
    public async Task Adapter_uses_shared_review_and_suppresses_foreign_or_late_answers(string fault)
    {
        var f = new Fixture();
        using var root = f.Root();
        var live = true;
        var runtime = new Runtime();
        var adapter = new ClaudeModelTurnAdapter(runtime, f.Reviews)
            .Bind(f.Reviews.PrepareClaudeTurn(f.Context, f.Profile), _ => ValueTask.FromResult(live));
        var offered = 0;
        f.Reviews.ClaudeOffered += review =>
        {
            offered++;
            using var presentation = review.Presentation();
            f.Reviews.Confirm(review, true, RequestOrigin.ActivatedVoice).Should().BeFalse();
            f.Reviews.Confirm(review, true, RequestOrigin.LocalUi).Should().BeTrue();
            presentation.Complete(HostOperationOutcome.Completed);
        };
        runtime.Run = async (provenance, profile, admit) =>
        {
            var transmission = new ClaudeTransmission(fault is "wrong-transmission" ? provenance with { Turn = new(Guid.NewGuid()) } : provenance,
                fault is "wrong-profile" ? profile with { Account = new("foreign") } : profile,
                new("https://api.anthropic.com/v1/messages"), new byte[] { 1 }, []);
            var approved = await admit(transmission, Token);
            if (fault is "late-answer") { live = false; }
            return new(approved && fault is not "runtime-denied" ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.DeniedEgress, "genuine");
        };
        var result = await adapter.RunAsync(f.Provenance, f.Profile, f.Context.Serialize(),
            (_, _, _) => throw new InvalidOperationException("No tool authority."), Token);
        result.Outcome.Should().Be(fault is "success" ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.DeniedEgress);
        result.Response?.Answer.Should().Be(fault is "success" ? "genuine" : null);
        offered.Should().Be(fault is "wrong-transmission" or "wrong-profile" ? 0 : 1);
        runtime.Calls.Should().Be(1);
    }

    internal sealed class Fixture : ISecurityAuditLog
    {
        internal MutableTime Time { get; } = new();
        internal HostRequest Request { get; } = HostRequest.Create(RequestOrigin.LocalUi);
        internal ProviderProfileTuple Profile { get; } = new(ProviderService.Claude, new("exact-account"), new(1),
            new("exact-model"), new("disabled"), new("8192"));
        internal ModelContextEnvelope Context { get; }
        internal ModelTurnProvenance Provenance { get; }
        internal HostedTransmissionReviewService Reviews { get; }
        internal List<SecurityAuditEvent> Audits { get; } = [];
        internal Fixture()
        {
            Context = new(Request, "policy", "original body", [], Time.Now, Time.Now.AddMinutes(1), ModelToolMode.None);
            Provenance = new(new(Guid.NewGuid()), Request, new(ModelProviderIdentity.Claude, new(Guid.NewGuid()), new(1)));
            Reviews = new(this, Time);
        }
        internal HostActivity Root() => HostActivity.BeginRoot(Request, HostActivityLayer.Application, HostOperation.Request);
        public void Write(SecurityAuditEvent auditEvent) { HostActivity.RequireCurrent(); Audits.Add(auditEvent); }
    }
    internal sealed class MutableTime : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Runtime : IClaudeTurnRuntime
    {
        public bool IsQuiescent => true;
        internal int Calls { get; private set; }
        internal Func<ModelTurnProvenance, ProviderProfileTuple, Func<ClaudeTransmission, CancellationToken, ValueTask<bool>>, Task<ClaudeTurnReply>>? Run { get; set; }
        public Task<ClaudeTurnReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile, ModelContextEnvelope context,
            Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit, Func<CancellationToken, ValueTask<bool>> current, CancellationToken token)
        { Calls++; return Run?.Invoke(provenance, profile, admit) ?? Task.FromResult(new ClaudeTurnReply(ModelTurnOutcome.DeniedEgress)); }
    }
}
