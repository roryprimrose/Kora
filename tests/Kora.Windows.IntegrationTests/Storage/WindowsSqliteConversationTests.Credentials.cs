using System.Text;
using System.Security.Cryptography;

using AwesomeAssertions;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteConversationTests
{
    [Fact]
    public async Task RequiredCredentialRejectionAuditFailureCannotRetainBodyOrClaimAReceipt()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        f.Reopen(new RejectionAuditFailure());
        await f.Store.InitializeAsync(f.Token);
        var request = Fresh(f.Request.SessionId);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.Invoking(store => store.RetainConversationMessageAsync(request, new(1), 0,
            "client_secret=synthetic-failure-only", static () => true, f.Token).AsTask()).Should().ThrowAsync<IOException>();
        f.Count("conversation_messages").Should().Be(0);
        (await f.Tasks.ReadTaskAsync(request.TaskId, f.Token)).Should().BeNull();
    }

    private sealed class RejectionAuditFailure : Kora.Windows.Storage.IHostInteractionTransactionCheckpoint
    {
        public void BeforeAudit(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction transaction) =>
            throw new IOException("required audit failed");
        public void BeforeCommit(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction transaction) { }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedCandidateNeverEntersBodyStorageAuditTextHashHistoryOrPreferences(bool draft)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var session = f.Request.SessionId;
        var request = Fresh(session);
        var candidate = "api_key=synthetic-only-rejection-marker";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(candidate)));
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var expected = await f.Store.ReadConversationDraftAsync(session, f.Token);
        if (draft)
        {
            await f.Store.RecordControlIntentAsync(request, f.Token);
            await f.Store.Invoking(store => store.CommitConversationDraftAsync(request, expected, candidate,
                static () => true, f.Token).AsTask()).Should().ThrowAsync<ConversationCredentialException>();
        }
        else
        {
            await f.Store.Invoking(store => store.RetainConversationMessageAsync(request, new(1), 0, candidate,
                static () => true, f.Token).AsTask()).Should().ThrowAsync<ConversationCredentialException>();
        }
        f.Count("conversation_messages").Should().Be(0);
        f.Count("conversation_drafts").Should().Be(0);
        (await f.Tasks.ReadTaskAsync(request.TaskId, f.Token))!.State.Should().Be(HostTaskState.Denied);
        var audit = AuditText(f);
        audit.Contains(candidate, StringComparison.Ordinal).Should().BeFalse();
        audit.Contains(hash, StringComparison.Ordinal).Should().BeFalse();
        audit.Should().Contain("credential-like-content").And.Contain("RequestedAudit");
        Encoding.UTF8.GetString(File.ReadAllBytes(f.DatabasePath)).Contains(candidate, StringComparison.Ordinal).Should().BeFalse();
        foreach (var path in Directory.EnumerateFiles(f.Paths.LocalRoot, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(path).EndsWith(".db", StringComparison.Ordinal)) { continue; }
            Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains(candidate, StringComparison.Ordinal).Should().BeFalse();
        }
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await f.Store.ReadConversationDraftAsync(session, f.Token)).Draft.Revision.Should().Be(0);
    }
}
