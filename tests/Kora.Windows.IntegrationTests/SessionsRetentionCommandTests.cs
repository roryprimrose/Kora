using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed class SessionsRetentionCommandTests
{
    [WindowsFact]
    public async Task VoiceHandoffToSameNativeReviewRequiresNewNativeDecisionAndNeverResumesDoneOrRenewsDueDates()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var workspace = WindowsSqliteSessionWorkspaceTests.Service(fixture, access);
        var id = fixture.Request.SessionId;
        var done = await workspace.ChangeLifecycleAsync(id, new(1), false, RequestOrigin.LocalUi, fixture.Token);
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths);
        sink.Initialize();
        var viewer = new SessionsViewModel(workspace,
            new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), access, fixture.Time,
                NullLogger<DurableEvidenceQuery>.Instance), access, NullLogger<SessionsViewModel>.Instance);
        var commands = new SessionRetentionCommandService(workspace, access, fixture.Time);
        var before = await fixture.Store.ReadRetentionObservationAsync(id, fixture.Token);
        foreach (var keep in new[] { true, false })
        {
            if (!keep) { fixture.Time.Now = before.State.DeleteDue; }
            var audits = fixture.Count("security_audit_events");
            var review = (await commands.ExecuteAsync(new(SessionCommandOperation.RetentionReview, id.Value) { KeepRetention = keep },
                RequestOrigin.ActivatedVoice, () => true, viewer.OpenRetentionReviewAsync, fixture.Token)).RetentionReview!;
            var result = await commands.ExecuteAsync(new(SessionCommandOperation.RetentionConfirm, id.Value)
                { RetentionReviewId = review.Id }, RequestOrigin.LocalUi, () => true, viewer.OpenRetentionReviewAsync, fixture.Token);
            result.Outcome.Should().Be("native-review");
            viewer.SelectedSessionRecord.Should().BeNull();
            viewer.Detail.Should().BeEmpty();
            viewer.CanResume.Should().BeFalse();
            viewer.RetentionPreview.Should().Contain("ActivatedVoice").And.Contain("NEW native decision");
            viewer.CanConfirmRetention.Should().BeTrue();
            fixture.Count("security_audit_events").Should().Be(audits, "commands only observe and open; they cannot write native retention audits");
            (await fixture.Store.ReadRetentionObservationAsync(id, fixture.Token)).State.Should().Be(before.State with { Perpetual = !keep });
            // A separate native event enters the existing LocalUi-only action after the original command has ended.
            await viewer.ConfirmRetentionAsync();
            viewer.Status.Should().Contain("readback committed");
            (await fixture.Store.ReadRetentionObservationAsync(id, fixture.Token)).State.Should().Be(before.State with { Perpetual = keep });
            (await fixture.Store.ReadSessionAsync(id, fixture.Token)).Should().Be(done);
        }
        viewer.Close();
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("admission")]
    [InlineData("stale")]
    [InlineData("control")]
    public async Task NativeHandoffRefusesUnusableSurfaceOrChangedExactObservation(string failure)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var workspace = WindowsSqliteSessionWorkspaceTests.Service(fixture, access);
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths);
        sink.Initialize();
        var viewer = new SessionsViewModel(workspace,
            new(new WindowsSqliteEvidenceReader(sink), access, fixture.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance);
        var observed = await fixture.Store.ReadRetentionObservationAsync(fixture.Request.SessionId, fixture.Token);
        if (failure is "closed") { viewer.Close(); }
        if (failure is "control") { access.CanControl = false; }
        var review = new Kora.Core.Storage.SessionRetentionReview(Guid.NewGuid(),
            failure is "stale" ? observed with { ExemptionAuditSequence = 99 } : observed,
            true, RequestOrigin.ActivatedVoice, fixture.Time.Now.AddSeconds(60));
        var action = () => viewer.OpenRetentionReviewAsync(review, () => failure is not "admission", fixture.Token);
        await action.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.Store.ReadRetentionObservationAsync(fixture.Request.SessionId, fixture.Token)).State.Perpetual.Should().BeFalse();
        viewer.CanConfirmRetention.Should().BeFalse();
        viewer.Close();
    }
}
