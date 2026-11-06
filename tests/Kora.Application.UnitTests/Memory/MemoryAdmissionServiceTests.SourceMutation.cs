using AwesomeAssertions;

using Kora.Application.Memory;
using Kora.Application.Memory.ModelProposals;
using Kora.Core.Diagnostics;
using Kora.Core.Memory;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Memory;

public sealed partial class MemoryAdmissionServiceTests
{
    [Theory]
    [InlineData("propose", false)]
    [InlineData("accept", false)]
    [InlineData("reject", false)]
    [InlineData("admit", false)]
    [InlineData("propose", true)]
    [InlineData("accept", true)]
    [InlineData("reject", true)]
    [InlineData("admit", true)]
    public async Task SourceBoundModelMutationRetiresMemoryUseUntilJoinedAndRequiresFreshReview(string operation, bool profile)
    {
        using var f = new Fixture(durable: true);
        var retained = await CreateRetainedContextMemory(f, profile);
        await using var management = f.Management(new ConcurrentMemoryAudit(f));
        using var workflow = profile ? management.CreateDeviceProfileContextWorkflow() : management.CreateContextWorkflow();
        var target = new SessionWorkspaceEntry(f.Session, null);
        var source = f.ModelSource = CreateModelSource(f);
        ModelMemoryProposalReview? proposal = null;
        if (operation is not "propose")
        {
            using (f.Root()) { proposal = await management.ReviewModelProposalAsync(target, source, static () => true, Token); }
            if (operation is "admit")
            {
                using (f.Root()) { await management.ActOnModelProposalAsync(proposal, false, true, Token); }
            }
        }
        using (proposal)
        {
            var selection = new[] { Summary(retained) };
            var original = await workflow.ReviewAsync(target, selection, "original question", static () => true, Token);
            var read = new TaskCompletionSource<Kora.Core.Conversation.ConversationMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.ModelSourceRead = read.Task;
            using var input = f.Root();
            var mutation = operation is "propose"
                ? management.ReviewModelProposalAsync(target, source, static () => true, Token)
                : (Task)management.ActOnModelProposalAsync(proposal!, operation is "admit", operation is "accept", Token);
            mutation.IsCompleted.Should().BeFalse();
            try
            {
                original.Revocation.IsCancellationRequested.Should().BeTrue();
                original.Records.Should().BeEmpty();
                original.UserRequest.Should().BeEmpty();
                var intents = f.ControlIntents.Count;
                var audits = f.Audits.Count;
                await workflow.Invoking(value => value.ReviewAsync(target, selection, "replacement question",
                    static () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
                f.ControlIntents.Should().HaveCount(intents);
                f.Audits.Should().HaveCount(audits);
                workflow.Invoking(value => value.Confirm(original, static () => true, Token))
                    .Should().Throw<InvalidOperationException>();
                f.Stored[retained.Id].Should().Be(retained);
            }
            finally
            {
                read.TrySetResult(source);
                await mutation;
                if (mutation is Task<ModelMemoryProposalReview> creation) { (await creation).Dispose(); }
                else { ((await (Task<MemoryResult>)mutation).Outcome).Should().Be(MemoryOutcome.Succeeded); }
            }
            original.Current.Should().BeFalse();
            f.ModelSourceRead = null;
            var fresh = await workflow.ReviewAsync(target, selection, "fresh question", static () => true, Token);
            fresh.Records.Should().Equal(retained);
            using var use = workflow.Confirm(fresh, static () => true, Token);
            use.Current().Should().BeTrue();
            f.Stored[retained.Id].Should().Be(retained);
            f.Audits.Should().NotContain(row => row.ActionId == "memory.use");
            f.Messages.Should().NotContain(text => text.Contains(Candidate.Value!, StringComparison.Ordinal));
            HostActivity.Current.Should().BeSameAs(input);
        }
    }

    [Theory]
    [InlineData("propose", false)]
    [InlineData("accept", false)]
    [InlineData("reject", false)]
    [InlineData("admit", false)]
    [InlineData("propose", true)]
    [InlineData("accept", true)]
    [InlineData("reject", true)]
    [InlineData("admit", true)]
    public async Task SourceBoundCopyMutationClosesBothUseScopesButReadOnlyInspectionPreservesConsent(string operation, bool profile)
    {
        using var f = new Fixture(durable: true);
        var profileMemory = profile ? await CreateRetainedContextMemory(f, profile) : null;
        await using var management = f.Management(new ConcurrentMemoryAudit(f));
        var source = await InspectCopySource(f, management);
        var retained = profileMemory ?? source;
        var target = new SessionWorkspaceEntry(f.Session, null);
        SessionMemoryCopyReview copy;
        using (f.Root()) { copy = await management.InspectSessionCopyAsync(target, source, static () => true, Token); }
        using (copy)
        {
            if (operation is not "propose")
            {
                using (f.Root()) { await management.ProposeSessionCopyAsync(copy, MemoryContentClass.Decision, Token); }
                if (operation is "admit")
                {
                    using (f.Root()) { await management.ReviewSessionCopyAsync(copy, true, Token); }
                }
            }
            using var workflow = profile ? management.CreateDeviceProfileContextWorkflow() : management.CreateContextWorkflow();
            var selection = new[] { Summary(retained) };
            var original = await workflow.ReviewAsync(target, selection, "original question", static () => true, Token);
            using (f.Root())
            using (await management.InspectSessionCopyAsync(target, source, static () => true, Token))
            {
                original.Current.Should().BeTrue("read-only source inspection is not a memory mutation");
                original.Revocation.IsCancellationRequested.Should().BeFalse();
            }
            var read = new TaskCompletionSource<SessionWorkspaceEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Pending = read.Task;
            using var input = f.Root();
            var mutation = operation switch
            {
                "propose" => management.ProposeSessionCopyAsync(copy, MemoryContentClass.Decision, Token),
                "admit" => management.AdmitSessionCopyAsync(copy, Token),
                _ => management.ReviewSessionCopyAsync(copy, operation is "accept", Token),
            };
            mutation.IsCompleted.Should().BeFalse();
            try
            {
                original.Revocation.IsCancellationRequested.Should().BeTrue();
                original.Records.Should().BeEmpty();
                original.UserRequest.Should().BeEmpty();
                var intents = f.ControlIntents.Count;
                var audits = f.Audits.Count;
                await workflow.Invoking(value => value.ReviewAsync(target, selection, "replacement question",
                    static () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
                f.ControlIntents.Should().HaveCount(intents);
                f.Audits.Should().HaveCount(audits);
                workflow.Invoking(value => value.Confirm(original, static () => true, Token))
                    .Should().Throw<InvalidOperationException>();
            }
            finally
            {
                f.Pending = null;
                read.TrySetResult(target);
                (await mutation).Outcome.Should().Be(MemoryOutcome.Succeeded);
            }
            original.Current.Should().BeFalse();
            var fresh = await workflow.ReviewAsync(target, selection, "fresh question", static () => true, Token);
            fresh.Records.Should().Equal(retained);
            using var use = workflow.Confirm(fresh, static () => true, Token);
            use.Current().Should().BeTrue();
            f.Stored[retained.Id].Should().Be(retained);
            f.Stored[source.Id].Should().Be(source);
            f.Audits.Should().NotContain(row => row.ActionId == "memory.use");
        }
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("commit")]
    [InlineData("audit")]
    [InlineData("privacy")]
    [InlineData("dispose")]
    public async Task SourceBoundAdmissionKeepsUseClosedUntilTransactionJoinsWithoutRestoringConsent(string failure)
    {
        using var f = new Fixture(durable: true);
        var retained = await CreateRetainedContextMemory(f, profile: false);
        await using var management = f.Management(new ContextAudit(f));
        using var workflow = management.CreateContextWorkflow();
        var target = new SessionWorkspaceEntry(f.Session, null);
        var source = f.ModelSource = CreateModelSource(f);
        ModelMemoryProposalReview proposal;
        using (f.Root()) { proposal = await management.ReviewModelProposalAsync(target, source, static () => true, Token); }
        using (proposal)
        {
            using (f.Root()) { await management.ActOnModelProposalAsync(proposal, false, true, Token); }
            var selection = new[] { Summary(retained) };
            var original = await workflow.ReviewAsync(target, selection, "original question", static () => true, Token);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var transactions = 0;
            f.BeforeStoreTransaction = _ =>
            {
                if (++transactions != 2) { return Task.CompletedTask; }
                entered.SetResult();
                return release.Task;
            };
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
            using var input = f.Root();
            var mutation = management.ActOnModelProposalAsync(proposal, true, false, cancellation.Token);
            await entered.Task.WaitAsync(Token);
            Task? closing = null;
            try
            {
                original.Revocation.IsCancellationRequested.Should().BeTrue();
                original.Records.Should().BeEmpty();
                if (failure is "cancel") { cancellation.Cancel(); }
                if (failure is "commit") { f.FailStoreCommit = true; }
                if (failure is "audit") { f.FailTerminalAudit = true; }
                if (failure is "privacy") { f.InspectEnabled = false; }
                if (failure is "dispose") { closing = management.DisposeAsync().AsTask(); }
                mutation.IsCompleted.Should().BeFalse("cancellation and closure do not join the physical transaction");
                if (closing is not null) { closing.IsCompleted.Should().BeFalse(); }
                var intents = f.ControlIntents.Count;
                var audits = f.Audits.Count;
                await workflow.Invoking(value => value.ReviewAsync(target, selection, "replacement question",
                    static () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
                f.ControlIntents.Should().HaveCount(intents);
                f.Audits.Should().HaveCount(audits);
            }
            finally
            {
                release.TrySetResult();
                await FluentActions.Awaiting(() => mutation).Should().ThrowAsync<Exception>();
                if (closing is not null) { await closing; }
                f.BeforeStoreTransaction = null;
                f.FailStoreCommit = false;
                f.FailTerminalAudit = false;
                f.InspectEnabled = true;
            }
            original.Current.Should().BeFalse();
            f.Stored[retained.Id].Should().Be(retained);
            if (failure is not "dispose")
            {
                var fresh = await workflow.ReviewAsync(target, selection, "fresh question", static () => true, Token);
                fresh.Records.Should().Equal(retained);
                using var use = workflow.Confirm(fresh, static () => true, Token);
                use.Current().Should().BeTrue();
            }
            f.Audits.Should().NotContain(row => row.ActionId == "memory.use");
            f.Messages.Should().NotContain(text => text.Contains(Candidate.Value!, StringComparison.Ordinal));
        }
    }

    private static async Task<MemoryRecord> CreateRetainedContextMemory(Fixture f, bool profile)
    {
        using var input = f.Root();
        using var owner = f.CreateService(true, profile ? MemoryScopeKind.DeviceProfile : MemoryScopeKind.Session);
        var scope = profile ? MemoryScope.DeviceProfile(f.Profile) : f.Scope;
        var proposed = (await owner.ProposeAsync(Candidate, scope, MemoryProposalOrigin.User, Token)).Record!;
        var accepted = (await owner.ReviewAsync(proposed.Id, proposed.Revision, true, Token)).Record!;
        return (await owner.AdmitAsync(accepted.Id, accepted.Revision, Token)).Record!;
    }
}
