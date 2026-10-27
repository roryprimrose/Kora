using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Fact]
    public async Task NativeConfirmationOfVoiceHistoryKeepsOriginalVoiceChannel()
    {
        using var f = new Fixture(RequestOrigin.ActivatedVoice);
        var earlier = Earlier(f);
        var host = QualifiedHistoryHost(f);
        var service = f.ConversationService(host, execution: host);
        using var review = await service.ReviewHistoryAsync(new(f.Session, null), "question",
            [new(earlier, ConversationHistoryRole.User)], static () => true, Token, RequestOrigin.ActivatedVoice);
        using var confirmation = HostActivity.BeginRoot(
            new(new(Guid.NewGuid()), f.Session.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            HostActivityLayer.Desktop, HostOperation.Request);
        var receipt = await service.SubmitHistoryAsync(review, static () => true, Token);
        receipt.Message.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        receipt.Message.AssistantResponse!.Provenance.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
    }

    [Fact]
    public async Task ClosureDuringFinalAdmissionMetadataReadCannotAcquireCapturePermission()
    {
        using var f = new Fixture();
        var earlier = Earlier(f);
        var host = QualifiedHistoryHost(f);
        var service = f.ConversationService(host, execution: host);
        var validations = 0;
        var live = true;
        var capture = 0;
        await service.Invoking(value => value.SubmitReviewedContextAsync(new(f.Session, null), "question",
            (request, now) => ConversationHistoryContext.CreateEnvelope(request, "question",
                [new(earlier, ConversationHistoryRole.User)], now, now.AddSeconds(15)),
            () => live, _ =>
            {
                if (++validations == 4) { f.OnRead = () => live = false; }
                return Task.CompletedTask;
            }, null, RequestOrigin.LocalUi, Token, _ => capture++))
            .Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        capture.Should().Be(0);
        f.ExecutionStarts.Should().Be(0);
    }

    [Theory]
    [InlineData("private")]
    [InlineData("target")]
    [InlineData("inactive")]
    [InlineData("initial-private")]
    public async Task RunningInspectionRefusesLatePrivateMetadataAndLifecycleChanges(string fault)
    {
        using var f = new Fixture();
        var service = f.ConversationService(QualifiedHistoryHost(f));
        var target = new SessionWorkspaceEntry(f.Session, null);
        f.OnRead = () =>
        {
            if (fault is "private") { f.CanControl = false; }
            if (fault is "target") { f.Metadata = new(f.Session.SessionId, new(1), new("changed")); }
        };
        if (fault is "inactive") { f.Session = f.Session with { IsActive = false }; target = new(f.Session, null); }
        if (fault is "initial-private") { f.CanControl = false; }
        await service.Invoking(value => value.ReadRunningConversationAsync(target, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RunningLeaseOfEarlierGenerationIsNeverDisclosedAsCurrentWork()
    {
        using var f = new Fixture();
        var host = QualifiedHistoryHost(f);
        var service = f.ConversationService(host, execution: host);
        var pending = new TaskCompletionSource<Kora.Application.Dependencies.ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = pending.Task;
        var submission = service.SubmitAsync(new(f.Session, null), "request", static () => true, Token);
        var original = f.Session;
        f.Session = f.Session with { Generation = new(2) };
        (await service.ReadRunningConversationAsync(new(f.Session, null), static () => true, Token)).Should().BeNull();
        f.Session = original;
        pending.SetResult(LateAnswer());
        (await submission).Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
    }

    [Fact]
    public async Task ReentrantAdmissionCannotReplaceAnExistingRunningLease()
    {
        using var f = new Fixture();
        var host = QualifiedHistoryHost(f);
        var service = f.ConversationService(new ReportedIdleAdmission(host), execution: host);
        Task<ConversationSubmissionReceipt>? competing = null;
        service.RunningConversationChanged += state =>
        {
            if (state.Phase != ConversationExecutionPhase.Running || competing is not null) { return; }
            var request = f.Request;
            var task = f.Task;
            var retained = f.Retained;
            competing = service.SubmitAsync(new(f.Session, null), "reentrant request", static () => true, Token);
            competing.IsCompleted.Should().BeTrue();
            f.Request = request;
            f.Task = task;
            f.Retained = retained;
        };
        (await service.SubmitAsync(new(f.Session, null), "original", static () => true, Token))
            .Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        await this.Invoking(_ => competing!).Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already accepting*");
        f.RetainCalls.Should().Be(1, "reentrant admission is refused before a second immutable intent can be retained");
        f.Adapter.Calls.Should().Be(1);
    }

    [Fact]
    public async Task RunningLeaseCanContinueRequiresOriginalAdmissionEvenWithoutCancellation()
    {
        using var f = new Fixture();
        var live = false;
        var target = new ConversationCancellationTarget(Guid.NewGuid(), f.Request, f.Session.Generation, new(2),
            f.Time.Now, f.Time.Now.AddMinutes(1));
        await using var lease = new ConversationCancellationLease(target, () => live, _ => { }, default, Token);
        lease.CanContinue.Should().BeFalse();
        live = true;
        lease.CanContinue.Should().BeTrue();
        await lease.RequestCancellationAsync(() => { });
        lease.CanContinue.Should().BeFalse();
    }
}