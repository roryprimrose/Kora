using System.Diagnostics;

using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Providers;

using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Dependencies;

[Collection("Host tracing")]
public sealed class OllamaAdmissionWorkflowTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public OllamaAdmissionWorkflowTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task ExactOriginalChannelReviewCommitsAuditedChoiceAndRevokesWithoutRestartAuthority(RequestOrigin origin)
    {
        await using var f = new OllamaAdmissionWorkflowFixture();
        var review = (await f.Workflow.Review(origin, () => f.Host.Eligible, f.Host.Token))!;
        review.Description.Should().Contain("thinking off").And.Contain("independently");
        f.Admitted.Should().BeNull();
        await f.Workflow.Confirm(review, origin, () => f.Host.Eligible, f.Host.Token);
        f.Admitted!().Should().BeTrue();
        f.Host.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Host.Audit.Events.Should().OnlyContain(item => item.Initiator ==
            (origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser));
        f.Host.Audit.Hosts.Should().OnlyContain(request => request != null && request.Origin == origin);
        f.Workflow.Status.Should().Contain("host lifetime");
        var persisted = LocalModelAdmissionPreference.Parse(f.Values["local-model-admission.json"]);
        persisted.EvidenceDigest.Should().Be(f.Evidence.Digest);
        persisted.Account.Should().Be(f.Candidate.Account);
        var active = f.Admitted;
        f.Host.Eligible = false;
        active!().Should().BeFalse();
        f.Host.Eligible = true;
        f.Values.Remove("local-model-admission.json");
        active().Should().BeFalse();
        await f.Workflow.Revoke(origin, () => true, f.Host.Token);
        f.Admitted.Should().BeNull();
        f.Values.Should().BeEmpty();
        f.Workflow.Status.Should().Contain("revoked");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("ambiguous")]
    [InlineData("expired")]
    [InlineData("runtime")]
    public async Task IndependentEvidenceAndExactCurrentSubjectAreRequiredBeforeReview(string fault)
    {
        await using var f = new OllamaAdmissionWorkflowFixture();
        if (fault is "missing") { f.Trusted = []; }
        if (fault is "ambiguous") { f.Trusted = [f.Evidence, f.Evidence]; }
        if (fault is "expired") { f.Host.Time.Now = f.Evidence.ExpiresAt; }
        if (fault is "runtime") { f.Available = false; }
        (await f.Review()).Should().BeNull();
        f.Workflow.Status.Should().Contain("Unavailable");
        f.Values.Should().BeEmpty();
    }

    [Fact]
    public async Task UnavailableAndFailedReviewsLogOnlyBoundedDiagnosticsWithoutGrantingAdmission()
    {
        await using var f = new OllamaAdmissionWorkflowFixture();
        f.Trusted = [];
        (await f.Review()).Should().BeNull();
        var unavailable = f.Diagnostics.Should().ContainSingle().Subject;
        unavailable.Level.Should().Be(LogLevel.Information);
        unavailable.EventId.Id.Should().Be(1);
        unavailable.Message.Should().Be("Local model admission unavailable: qualification-or-current-subject-missing.");

        f.Values["local-model-admission.json"] = "private-invalid-saved-content";
        await f.Invoking(value => value.Review()).Should().ThrowExactlyAsync<InvalidDataException>();
        f.Diagnostics.Should().HaveCount(2);
        var failed = f.Diagnostics[1];
        failed.Level.Should().Be(LogLevel.Error);
        failed.EventId.Id.Should().Be(2);
        failed.Message.Should().Be("Local model admission held unavailable after InvalidDataException; explicit review recovery required.");
        f.Admitted.Should().BeNull();
        f.Observations.Should().Be(0);
        f.Host.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("channel")]
    [InlineData("foreign")]
    [InlineData("expired")]
    [InlineData("clock-backwards")]
    [InlineData("eligibility")]
    [InlineData("original-eligibility")]
    [InlineData("evidence")]
    [InlineData("subject")]
    [InlineData("commit-expired")]
    [InlineData("session")]
    [InlineData("replay")]
    public async Task ReviewCannotSurviveChangedIdentityEvidenceChannelOrOriginalAuthority(string fault)
    {
        await using var f = new OllamaAdmissionWorkflowFixture();
        var originalEligible = true;
        var review = (await f.Workflow.Review(RequestOrigin.LocalUi, () => originalEligible, f.Host.Token))!;
        if (fault is "foreign") { review = review with { }; }
        if (fault is "expired") { f.Host.Time.Now = review.ExpiresAt; }
        if (fault is "clock-backwards") { f.Host.Time.Now = f.Evidence.IssuedAt.AddTicks(-1); }
        if (fault is "eligibility") { f.Host.Eligible = false; }
        if (fault is "original-eligibility") { originalEligible = false; }
        if (fault is "evidence") { f.Trusted = []; }
        if (fault is "subject") { f.Current = false; }
        if (fault is "commit-expired") { f.AfterCurrent = () => f.Host.Time.Now = review.ExpiresAt; }
        if (fault is "session") { f.AfterCurrent = () => f.Host.Store.Authority = f.Host.Store.Authority! with { IsActive = false }; }
        if (fault is "replay") { await f.Confirm(review); }
        await f.Workflow.Invoking(workflow => workflow.Confirm(review,
            fault is "channel" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi, () => f.Host.Eligible, f.Host.Token))
            .Should().ThrowAsync<Exception>();
        f.Admitted.Should().BeNull();
    }

    [Theory]
    [InlineData("marker-write")]
    [InlineData("choice-write")]
    [InlineData("readback")]
    [InlineData("requested-audit")]
    [InlineData("terminal-audit")]
    [InlineData("lost-after-audit")]
    [InlineData("confirm-delete")]
    [InlineData("confirm-readback")]
    public async Task PersistenceOrAuditFailureNeverPublishesAdmission(string fault)
    {
        await using var f = new OllamaAdmissionWorkflowFixture();
        var review = (await f.Review())!;
        if (fault is "marker-write") { f.FailWrite = "local-model-admission-unconfirmed.txt"; }
        if (fault is "choice-write") { f.FailWrite = "local-model-admission.json"; }
        if (fault is "readback") { f.AfterSave = () => f.Values.Remove("local-model-admission.json"); }
        if (fault is "requested-audit" or "terminal-audit")
        {
            f.Host.Audit.BeforeWrite = audit =>
            {
                if (audit.Outcome == (fault is "requested-audit" ? SecurityAuditOutcome.Requested : SecurityAuditOutcome.Succeeded))
                { throw new IOException("Required fixture audit unavailable."); }
            };
        }
        if (fault is "lost-after-audit")
        {
            f.Host.Audit.BeforeWrite = audit => { if (audit.Outcome == SecurityAuditOutcome.Succeeded) { f.Host.Eligible = false; } };
        }
        if (fault is "confirm-delete") { f.FailDelete = "local-model-admission-unconfirmed.txt"; }
        if (fault is "confirm-readback") { f.AfterConfirm = () => f.Values.Remove("local-model-admission.json"); }
        await f.Invoking(value => value.Confirm(review)).Should().ThrowAsync<Exception>();
        f.Admitted.Should().BeNull();
        f.Workflow.Status.Should().Contain("unconfirmed");
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("corrupt")]
    [InlineData("cancelled")]
    public async Task InvalidSavedStateOrCancellationCannotObserveRuntime(string fault)
    {
        await using var f = new OllamaAdmissionWorkflowFixture();
        if (fault is "pending") { f.Values["local-model-admission-unconfirmed.txt"] = "1"; }
        if (fault is "corrupt") { f.Values["local-model-admission.json"] = "{"; }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Host.Token);
        if (fault is "cancelled") { await cancellation.CancelAsync(); }
        await f.Workflow.Invoking(value => value.Review(RequestOrigin.LocalUi, () => true, cancellation.Token))
            .Should().ThrowAsync<Exception>();
        f.Observations.Should().Be(0);
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("audit")]
    public async Task RevocationClosesLiveAdmissionEvenWhenDurableRemovalFails(string fault)
    {
        await using var f = new OllamaAdmissionWorkflowFixture();
        await f.Confirm((await f.Review())!);
        if (fault is "remove") { f.FailDelete = "local-model-admission.json"; }
        if (fault is "audit") { f.Host.Audit.BeforeWrite = _ => throw new IOException("Audit fixture unavailable."); }
        await f.Invoking(value => value.RevokeAdmission()).Should().ThrowAsync<IOException>();
        f.Admitted.Should().BeNull();
    }

    [Fact]
    public async Task DisposalNeverRestoresDurableConsentAsAuthority()
    {
        await using var f = new OllamaAdmissionWorkflowFixture();
        await f.Confirm((await f.Review())!);
        var active = f.Admitted!;
        await f.Workflow.DisposeAsync();
        active().Should().BeFalse();
        f.Admitted.Should().BeNull();
        await using var restarted = f.NewWorkflow();
        f.Admitted.Should().BeNull();
        f.Values.Should().ContainKey("local-model-admission.json");
        await f.Workflow.DisposeAsync();
    }

    [Fact]
    public async Task DisposalCancelsPendingObservationAndWaitsForItsActualTermination()
    {
        await using var f = new OllamaAdmissionWorkflowFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.BeforeObserve = async token =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        };
        var pending = f.Review();
        await Task.WhenAny(entered.Task, pending).WaitAsync(f.Host.Token);
        pending.IsCompleted.Should().BeFalse("the observation must be reached before exercising disposal");
        await f.Workflow.DisposeAsync();
        await pending.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();
        f.Admitted.Should().BeNull();
    }
}