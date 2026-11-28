using System.Security.Cryptography;
using System.Text;

using AwesomeAssertions;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteConversationTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("stale")]
    [InlineData("commit-drift")]
    [InlineData("audit-failure")]
    [InlineData("cancel")]
    public async Task VoiceReadbackPermissionAuditAndOriginalRetentionShareOneCommit(string boundary)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var policy = await f.Store.ReadSessionPermissionAsync(f.Request.SessionId, f.Token);
        var control = Fresh(f.Request.SessionId);
        using (var root = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RecordControlIntentAsync(control, f.Token);
            policy = await f.Store.SetSessionPermissionAsync(control, policy, SessionPermissionLevel.ApproveAll,
                static () => true, f.Token);
            var task = (await f.Tasks.ReadTaskAsync(control.TaskId, f.Token))!;
            if (!task.IsTerminal) { await f.Tasks.CommitAsync(task.Next(HostTaskState.Succeeded), task.Revision.Value, f.Token); }
        }
        if (boundary is "stale")
        {
            var next = Fresh(f.Request.SessionId);
            using var root = HostActivity.BeginRoot(next, HostActivityLayer.Application, HostOperation.Request);
            await f.Store.RecordControlIntentAsync(next, f.Token);
            await f.Store.SetSessionPermissionAsync(next, policy, SessionPermissionLevel.ReviewAll, static () => true, f.Token);
            var task = (await f.Tasks.ReadTaskAsync(next.TaskId, f.Token))!;
            if (!task.IsTerminal) { await f.Tasks.CommitAsync(task.Next(HostTaskState.Succeeded), task.Revision.Value, f.Token); }
        }
        var checkpoint = new BoundReadbackCheckpoint(boundary);
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        var audits = f.Count("security_audit_events");
        var tasks = f.Count("host_tasks");
        var request = new HostRequest(new(Guid.NewGuid()), f.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.ActivatedVoice);
        const string body = "  exact original readback body\r\nend  ";
        var approval = new AutomaticApprovalAudit(request, policy, "conversation.voice-readback",
            HostOperationEffect.BoundedWrite, "request-readback", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body))));
        using (var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            checkpoint.Armed = true;
            var retain = () => f.Store.RetainPermissionBoundConversationAsync(request, new(1), 0, body, [],
                policy, approval, () => !checkpoint.AdmissionLost, f.Token).AsTask();
            if (boundary is "success")
            {
                var receipt = await retain();
                receipt.Message.Body.Should().Be(body);
                receipt.Message.OrdinaryText.Should().BeTrue();
                receipt.Message.Request.Should().Be(request);
                await retain.Should().ThrowAsync<InvalidOperationException>();
                f.Count("conversation_messages").Should().Be(1);
                AuditText(f).Should().Contain("conversation.voice-readback").And.NotContain(body);
                f.Count("security_audit_events").Should().Be(audits + 2);
            }
            else
            {
                if (boundary is "stale" or "commit-drift")
                { await retain.Should().ThrowAsync<ISessionConversationStore.PermissionChangedException>(); }
                else if (boundary is "audit-failure") { await retain.Should().ThrowAsync<IOException>(); }
                else { await retain.Should().ThrowAsync<InvalidOperationException>(); }
                f.Count("conversation_messages").Should().Be(0);
                f.Count("host_tasks").Should().Be(tasks);
                f.Count("security_audit_events").Should().Be(audits);
            }
        }
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await f.Store.ReadSessionPermissionAsync(request.SessionId, f.Token)).Should().Be(
            boundary is "stale" ? policy with { Revision = new(policy.Revision.Value + 1), Level = SessionPermissionLevel.ReviewAll } : policy);
    }

    private sealed class BoundReadbackCheckpoint(string boundary) : IHostInteractionTransactionCheckpoint
    {
        internal bool Armed { get; set; }
        internal bool AdmissionLost { get; private set; }
        public void BeforeAudit(SqliteConnection connection, SqliteTransaction transaction)
        {
            if (Armed && boundary is "audit-failure") { throw new IOException("Synthetic authoritative audit failure"); }
        }
        public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction)
        {
            if (!Armed) { return; }
            if (boundary is "cancel") { AdmissionLost = true; }
            if (boundary is "commit-drift")
            {
                using var change = connection.CreateCommand();
                change.Transaction = transaction;
                change.CommandText = "DELETE FROM session_permission;";
                change.ExecuteNonQuery().Should().Be(1);
            }
        }
    }
}
