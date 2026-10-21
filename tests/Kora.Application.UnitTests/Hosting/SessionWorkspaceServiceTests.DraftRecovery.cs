using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("known-target")]
    [InlineData("unavailable")]
    [InlineData("cancel")]
    [InlineData("corrupt")]
    public async Task NativeConversationRecoveryDelegatesOnlyAdmittedReadsAndPreservesExplicitSourceFailure(string outcome)
    {
        using var f = new DraftRecoveryFixture();
        var original = f.Draft;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var service = new SessionConversationService(f, f, f.Service, f, new RecoveryTurns(), TimeProvider.System,
            NullLogger<SessionConversationService>.Instance);
        f.AfterDraftRead = () =>
        {
            if (outcome is "unavailable") { throw new IOException("private marker"); }
            if (outcome is "cancel") { throw new OperationCanceledException(); }
            if (outcome is "corrupt") { f.Draft = f.Draft with { Draft = f.Draft.Draft with { Version = 2 } }; }
            if (outcome is "known-target") { f.Draft = f.Draft with { ExpectedTarget = target }; }
        };
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var operation = () => service.ReadLegacyUnsentAsync(target, () => true, f.Token);
        if (outcome is "success" or "known-target")
        {
            (await operation()).Should().Be(original with { ExpectedTarget = target });
            f.DraftReads.Should().Be(1);
        }
        else if (outcome is "cancel") { await operation.Should().ThrowAsync<OperationCanceledException>(); }
        else if (outcome is "unavailable") { await operation.Should().ThrowAsync<IOException>(); }
        else { await operation.Should().ThrowAsync<InvalidDataException>(); }
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Fact]
    public async Task DraftRecoveryObservationIsExactPassiveAndCorrelatedWithoutControlWrites()
    {
        using var f = new DraftRecoveryFixture();
        var target = new SessionWorkspaceEntry(f.Session, null);
        var metadataReads = 0;
        f.ExactListReader = _ => { metadataReads++; return target; };
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var result = await f.Service.ReadConversationDraftForRecoveryAsync(target, () => true, f.Token);
        result.Should().Be(f.Draft with { ExpectedTarget = target });
        f.DraftReads.Should().Be(1);
        metadataReads.Should().Be(2);
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
        HostActivity.RequireCurrent().Request.Should().Be(f.Request);
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("invocation")]
    [InlineData("session")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("selection")]
    [InlineData("target")]
    [InlineData("metadata-control")]
    public async Task DraftRecoveryRequiresHostResolvedNativeExactSessionAndPrivateControlBeforeBodyRead(string fault)
    {
        using var f = new DraftRecoveryFixture();
        var target = new SessionWorkspaceEntry(f.Session, null);
        var request = new HostRequest(f.Request.RequestId,
            fault is "session" ? new(Guid.NewGuid()) : f.Session.SessionId, f.Request.TaskId,
            fault is "origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi,
            fault is "invocation" ? new(Guid.NewGuid()) : null);
        if (fault is "private") { f.CanInspect = false; }
        if (fault is "control") { f.CanControl = false; }
        f.ExactListReader = _ =>
        {
            if (fault is "metadata-control") { f.AdvanceRevision(); }
            return fault is "target" ? target with { Authority = target.Authority with { Generation = new(2) } } : target;
        };
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await f.Service.Invoking(service => service.ReadConversationDraftForRecoveryAsync(target, () => fault is not "selection", f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.DraftReads.Should().Be(0);
        f.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("metadata")]
    [InlineData("control")]
    [InlineData("private")]
    [InlineData("permission")]
    [InlineData("selection")]
    [InlineData("cancel")]
    [InlineData("read-failure")]
    [InlineData("draft-generation")]
    [InlineData("draft-session")]
    [InlineData("expected-target")]
    [InlineData("invalid")]
    public async Task DraftRecoveryRejectsLateSourceOrAdmissionChangesWithoutPublishingOrWriting(string fault)
    {
        using var f = new DraftRecoveryFixture();
        var target = new SessionWorkspaceEntry(f.Session, null);
        var admitted = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        f.AfterDraftRead = () =>
        {
            if (fault is "generation") { f.Session = f.Session with { Generation = new(2) }; }
            if (fault is "metadata") { f.ExactListReader = _ => target with { Metadata = new(f.Session.SessionId, new(1), new("changed")) }; }
            if (fault is "control") { f.AdvanceRevision(); }
            if (fault is "private") { f.CanInspect = false; }
            if (fault is "permission") { f.CanControl = false; }
            if (fault is "selection") { admitted = false; }
            if (fault is "cancel") { cancellation.Cancel(); }
            if (fault is "read-failure") { throw new IOException("Synthetic source failure."); }
            if (fault is "draft-generation") { f.Draft = f.Draft with { Session = f.Session with { Generation = new(2) } }; }
            if (fault is "draft-session") { f.Draft = f.Draft with { Draft = f.Draft.Draft with { SessionId = new(Guid.NewGuid()) } }; }
            if (fault is "expected-target") { f.Draft = f.Draft with { ExpectedTarget = target with { Authority = target.Authority with { Generation = new(2) } } }; }
            if (fault is "invalid") { f.Draft = f.Draft with { Draft = f.Draft.Draft with { Body = "\ud800" } }; }
        };
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        await f.Service.Invoking(service => service.ReadConversationDraftForRecoveryAsync(target, () => admitted, cancellation.Token))
            .Should().ThrowAsync<Exception>();
        f.DraftReads.Should().Be(1);
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    private sealed class DraftRecoveryFixture : Fixture, ISessionConversationStore
    {
        internal DraftRecoveryFixture() => Draft = new(Session, new(Session.SessionId, 1, "private exact saved body"));
        internal ConversationDraftSnapshot Draft { get; set; }
        internal Action? AfterDraftRead { get; set; }
        internal int DraftReads { get; private set; }

        public ValueTask<ConversationDraftSnapshot> ReadConversationDraftAsync(HostId<SessionIdentity> session, CancellationToken token)
        {
            session.Should().Be(Session.SessionId);
            HostActivity.RequireCurrent().Request.Should().Be(Request);
            DraftReads++;
            AfterDraftRead?.Invoke();
            return ValueTask.FromResult(Draft);
        }

        public ValueTask<HostTaskRecord> RecordConversationCredentialRejectionAsync(HostRequest request, HostRevision generation, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> CommitConversationDraftAsync(HostRequest control, ConversationDraftSnapshot expected, string? body, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationSubmissionReceipt> RetainConversationMessageAsync(HostRequest request, HostRevision generation, long expectedMetadataRevision, string body, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationSubmissionReceipt> CompleteConversationSubmissionAsync(HostRequest request, Kora.Core.Dependencies.ModelTurnOutcome outcome, Kora.Core.Dependencies.ModelTurnReason reason, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationPage> ReadConversationPageAsync(HostId<SessionIdentity> session, ConversationCursor? cursor, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationMessage> ReadConversationMessageAsync(HostId<SessionIdentity> session, HostId<RequestIdentity> request, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class RecoveryTurns : IModelTurnAdmission
    {
        public bool IsQuiescent => throw new InvalidOperationException("Draft recovery cannot inspect provider admission.");
        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
            ModelHandoffReview? review, CancellationToken token) =>
            throw new InvalidOperationException("Draft recovery cannot submit or admit a turn.");
    }
}
