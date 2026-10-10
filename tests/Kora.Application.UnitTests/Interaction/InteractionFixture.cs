using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Interaction;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Network;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Interaction;

internal sealed class InteractionFixture : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public InteractionFixture(
        RequestOrigin origin = RequestOrigin.LocalUi,
        IPreapprovedUriConfiguration? preapprovedUris = null)
    {
        ActivitySource.AddActivityListener(listener);
        Request = NewRequest(origin);
        Store = new(new(new(Request, new(1), HostTaskState.IntentRecorded),
            new(Request.SessionId, new(1), true), new(true, true, false, true),
            new(Request, new(Guid.NewGuid()), new(1), Binding(), HostOperationEffect.BoundedRead, Time.Now.AddMinutes(10)), [], []));
        Questions = new(Store, Time);
        Authorization = new(Store, Time, preapprovedUris);
    }

    public HostRequest Request { get; private set; }
    public TestTime Time { get; } = new();
    public TransactionalStore Store { get; }
    public HostQuestionService Questions { get; }
    public HostAuthorizationService Authorization { get; }

    public void Dispose() => listener.Dispose();

    public async Task<HostInteractionDecision> RunAsync(Func<ValueTask<HostInteractionDecision>> operation)
    {
        using var activity = HostActivity.BeginRoot(Request, HostActivityLayer.Application, HostOperation.Request);
        return await operation();
    }

    public async Task<HostQuestionRecord> QuestionAsync(QuestionSpec? spec = null)
    {
        var result = await RunAsync(() => Questions.CreateAsync(Request, spec ?? Choice(), Time.Now.AddMinutes(5), CancellationToken.None));
        result.Outcome.Should().Be(HostInteractionOutcome.Presented);
        return result.Question!;
    }

    public async Task<OperationGrant> GrantAsync(string scope = "once", RequestOrigin channel = RequestOrigin.LocalUi)
    {
        var presented = await RunAsync(() => Authorization.PresentAsync(Request, CancellationToken.None));
        var result = await RunAsync(() => Authorization.ApproveAsync(presented.Question!.Key, new([scope]), channel, CancellationToken.None));
        result.Outcome.Should().Be(HostInteractionOutcome.Approved);
        return result.Grant!;
    }

    public async Task<OperationGrant> GrantAfterPresentationAsync(HostQuestionRecord question)
    {
        var result = await RunAsync(() => Authorization.ApproveAsync(question.Key, new(["once"]), RequestOrigin.LocalUi, CancellationToken.None));
        result.Outcome.Should().Be(HostInteractionOutcome.Approved);
        return result.Grant!;
    }

    public void Rebind(bool newSession = false, RequestOrigin? origin = null)
    {
        Request = NewRequest(origin ?? Request.Origin, newSession ? null : Request.SessionId);
        Store.Change(snapshot => snapshot with
        {
            Intent = new(Request, new(1), HostTaskState.IntentRecorded),
            Session = newSession ? new(Request.SessionId, new(1), true) : snapshot.Session,
            Proposal = new(Request, new(Guid.NewGuid()), new(1), snapshot.Proposal!.Binding,
                snapshot.Proposal.Effect, Time.Now.AddMinutes(10)),
        });
    }

    public static QuestionSpec Choice() => new("Choose", QuestionKind.SingleChoice, [new("a", "First"), new("b", "Second")]);
    public static ExactOperationBinding Binding(int changedField = -1)
    {
        var digests = Enumerable.Repeat(new string('a', 64), 9).ToArray();
        if (changedField >= 0)
        {
            digests[changedField] = new string('b', 64);
        }
        return new("bounded.read", "builtin", "inspect", digests[0], digests[1], digests[2], digests[3],
            digests[4], digests[5], digests[6], digests[7], digests[8], new(1));
    }

    public void ChangeProposal(ExactOperationBinding? binding = null, HostOperationEffect? effect = null, HostRevision? revision = null)
    {
        Store.Change(snapshot => snapshot with { Proposal = new(Request, snapshot.Proposal!.ProposalId,
            revision ?? snapshot.Proposal.Revision, binding ?? snapshot.Proposal.Binding,
            effect ?? snapshot.Proposal.Effect, snapshot.Proposal.ExpiresAt) });
    }

    private static HostRequest NewRequest(RequestOrigin origin, HostId<SessionIdentity>? session = null) =>
        new(new(Guid.NewGuid()), session ?? new(Guid.NewGuid()), new(Guid.NewGuid()), origin, new(Guid.NewGuid()));

    internal sealed class TestTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 6, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    // Test-only adapter exercises the agreed atomic seam; this is not a production durable store/schema.
    internal sealed class TransactionalStore(HostInteractionSnapshot initial) : IHostInteractionStore
    {
        private readonly Lock sync = new();
        public HostInteractionSnapshot Snapshot { get; private set; } = initial;
        public List<HostInteractionCommit> Commits { get; } = [];
        public bool FailStorage { get; set; }
        public bool FailAudit { get; set; }
        public bool UncertainCommit { get; set; }
        public Action? BeforeCommit { get; set; }
        public Action? BeforeTransition { get; set; }
        public HostActivity? LastActivity { get; private set; }

        public void Change(Func<HostInteractionSnapshot, HostInteractionSnapshot> change)
        {
            lock (sync)
            {
                Snapshot = change(Snapshot);
            }
        }

        public ValueTask<HostInteractionDecision> TransactAsync(HostRequest request,
            Func<HostInteractionSnapshot, HostInteractionCommit> transition, CancellationToken cancellationToken)
        {
            lock (sync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LastActivity = HostActivity.RequireCurrent();
                LastActivity.Request.Should().Be(request);
                BeforeTransition?.Invoke();
                var commit = transition(Snapshot);
                BeforeCommit?.Invoke();
                cancellationToken.ThrowIfCancellationRequested();
                if (FailStorage || FailAudit)
                {
                    throw new IOException("Synthetic storage/audit unavailable.");
                }
                Snapshot = commit.Snapshot;
                Commits.Add(commit);
                if (UncertainCommit)
                {
                    throw new IOException("Commit certainty lost; do not retry or dispatch.");
                }
                return ValueTask.FromResult(commit.Decision);
            }
        }
    }
}
