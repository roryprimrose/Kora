using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed class SessionsRetentionApplyViewModelTests
{
    [Theory]
    [InlineData(0, SessionRetentionApplyEffect.DeadlinesOnly)]
    [InlineData(2, SessionRetentionApplyEffect.Archive)]
    [InlineData(3, SessionRetentionApplyEffect.Delete)]
    public async Task OriginalNativeReviewSeparateConfirmationAndDurableEffectCompleteWithoutARequest(int age,
        SessionRetentionApplyEffect effect)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, Kora.Core.Hosting.HostTaskState.Succeeded);
        var id = f.Request.SessionId;
        var before = await f.Store.ReadRetentionAsync(id, f.Token);
        var policy = new SessionRetentionPolicy();
        f.Reopen(sessionRetentionPolicy: policy);
        await f.Store.InitializeAsync(f.Token);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var workspace = WindowsSqliteSessionWorkspaceTests.Service(f, access);
        await using var admission = new DiagnosticRetentionAdmission(f.Store, f.Store, new(f.Tasks));
        var preferences = new Preferences();
        var configuration = new SessionRetentionConfigurationService(preferences, policy, admission, new NoAudit());
        configuration.Observe();
        var modelAdmission = new NoModel();
        await using var conversation = new SessionConversationService(f.Store, f.Store, workspace, access,
            modelAdmission, f.Time, NullLogger<SessionConversationService>.Instance);
        var apply = new SessionRetentionApplyService(workspace, conversation, configuration, access);
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var viewer = new SessionsViewModel(workspace,
            new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), access, f.Time,
                NullLogger<DurableEvidenceQuery>.Instance), access, NullLogger<SessionsViewModel>.Instance,
            conversation: conversation, retentionApply: apply);
        f.Time.Now = before.LastMeaningfulActivity.AddDays(age);
        viewer.HistorySessionId = id.Value.ToString("D");
        await viewer.ReadExactRetentionAsync();
        viewer.CanReviewRetentionApply.Should().BeTrue();
        var audits = f.Count("security_audit_events");
        await viewer.ReviewRetentionApplyAsync();
        viewer.CanConfirmRetentionApply.Should().BeTrue();
        viewer.RetentionApplyPreview.Should().Contain(effect.ToString()).And.Contain("Inventory:");
        (await f.Store.ReadRetentionAsync(id, f.Token)).Should().Be(before);
        f.Count("security_audit_events").Should().Be(audits);
        viewer.CancelRetentionApplication();
        viewer.CanConfirmRetentionApply.Should().BeFalse();
        await viewer.ReviewRetentionApplyAsync();
        await viewer.ConfirmRetentionApplyAsync();
        viewer.Status.Should().Contain(effect + " completed");
        var after = await f.Store.ReadRetentionAsync(id, f.Token);
        after.LastMeaningfulActivity.Should().Be(before.LastMeaningfulActivity);
        after.ArchiveDue.Should().Be(before.LastMeaningfulActivity.AddDays(1));
        after.DeleteDue.Should().Be(before.LastMeaningfulActivity.AddDays(3));
        preferences.Writes.Should().Be(0);
        viewer.Close();
        viewer.CanConfirmRetentionApply.Should().BeFalse();
    }

    private sealed class NoModel : IModelTurnAdmission
    {
        public bool IsQuiescent => true;
        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
            ModelHandoffReview? review, CancellationToken token) =>
            throw new InvalidOperationException("Apply-now must not call a model or submit a conversation.");
    }

    private sealed class NoAudit : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) => throw new InvalidOperationException("No preference writes are permitted.");
    }

    private sealed class Preferences : ISessionRetentionPreferences
    {
        internal int Writes { get; private set; }
        public SessionRetentionSettings? Load() => new(1, 3);
        public SessionRetentionSettings? ReadBack() => new(1, 3);
        public void BeginWrite() => Writes++;
        public void ConfirmWrite() => Writes++;
        public void Save(SessionRetentionSettings value) => Writes++;
        public void Reset() => Writes++;
    }
}
