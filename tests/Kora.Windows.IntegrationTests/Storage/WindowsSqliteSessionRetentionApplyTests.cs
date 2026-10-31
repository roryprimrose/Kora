using AwesomeAssertions;

using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed class WindowsSqliteSessionRetentionApplyTests
{
    [Theory]
    [InlineData(0, SessionRetentionApplyEffect.DeadlinesOnly)]
    [InlineData(2, SessionRetentionApplyEffect.Archive)]
    [InlineData(3, SessionRetentionApplyEffect.Delete)]
    public async Task ExactReviewAndSeparateApplyCommitOnlyReviewedPolicyAndDueAction(int age, SessionRetentionApplyEffect effect)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var perpetual = await f.GrantAsync("perpetual");
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var id = f.Request.SessionId;
        const string body = "OWNED_RETENTION_APPLY_ORIGINAL_5CFD8";
        f.Request = new(new(Guid.NewGuid()), id, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        await f.RunAsync(async () =>
        {
            await f.Store.RetainConversationMessageAsync(f.Request, new(1), 0, body, () => true, f.Token);
            await f.Store.CompleteConversationSubmissionAsync(f.Request, Kora.Core.Dependencies.ModelTurnOutcome.Cancelled,
                Kora.Core.Dependencies.ModelTurnReason.CallerCancelled, () => true, f.Token);
        });
        var before = await f.Store.ReadRetentionAsync(id, f.Token);
        var workspace = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        var other = await workspace.CreateAsync(new("Unrelated"), RequestOrigin.LocalUi, f.Token);
        var otherClock = await f.Store.ReadRetentionAsync(other.Authority.SessionId, f.Token);
        f.Time.Now = before.LastMeaningfulActivity.AddDays(age);
        var policy = new SessionRetentionPolicy();
        policy.Activate(new(1, 3));
        f.Reopen(sessionRetentionPolicy: policy);
        await f.Store.InitializeAsync(f.Token);
        var audits = f.Count("security_audit_events");
        var review = await f.Store.ReviewRetentionApplyAsync(id, f.Token);
        review.Before.State.Should().Be(before);
        review.Inventory.ConversationMessages.Should().Be(1);
        review.Effect.Should().Be(effect);
        (await f.Store.ReadRetentionAsync(id, f.Token)).Should().Be(before);
        f.Count("security_audit_events").Should().Be(audits);
        await f.Store.ValidateRetentionApplyAsync(review, f.Token);
        var receipt = await Apply(f, review);
        receipt.Retention.State.Should().Be(review.After);
        receipt.Retention.State.LastMeaningfulActivity.Should().Be(before.LastMeaningfulActivity);
        receipt.Retention.Removed.Should().Be(effect == SessionRetentionApplyEffect.Delete);
        receipt.Retention.Generation.Value.Should().Be(effect == SessionRetentionApplyEffect.DeadlinesOnly ? 1 : 2);
        (await f.Store.ReadRetentionAsync(other.Authority.SessionId, f.Token)).Should().Be(otherClock);
        (await f.Store.ReadGrantsAsync(f.Token)).Should().Contain(perpetual);
        f.Count("security_audit_events").Should().Be(audits + (effect == SessionRetentionApplyEffect.DeadlinesOnly ? 2 : 3));
        if (effect == SessionRetentionApplyEffect.Delete)
        {
            new FileInfo(f.DatabasePath + "-journal").Length.Should().Be(0);
            System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(f.DatabasePath)).Should().NotContain(body);
        }
        else
        {
            (await f.Store.ReadConversationPageAsync(id, null, 25, f.Token)).Messages.Single().Body.Should().Be(body);
        }
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await f.Store.ReadRetentionAsync(id, f.Token)).Should().Be(review.After);
        await f.Store.Invoking(store => store.ValidateRetentionApplyAsync(review, f.Token).AsTask())
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("head")]
    [InlineData("generation")]
    [InlineData("hold")]
    [InlineData("inventory")]
    [InlineData("cutoff")]
    [InlineData("rollback-clock")]
    [InlineData("due-drift")]
    [InlineData("source")]
    public async Task DriftInvalidatesReviewWithoutPolicyOrAction(string fault)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var policy = new SessionRetentionPolicy();
        policy.Activate(new(1, 3));
        f.Reopen(sessionRetentionPolicy: policy);
        await f.Store.InitializeAsync(f.Token);
        var id = f.Request.SessionId;
        var before = await f.Store.ReadRetentionAsync(id, f.Token);
        if (string.Equals(fault, "due-drift", StringComparison.Ordinal)) { f.Time.Now = before.LastMeaningfulActivity.AddDays(1).AddSeconds(-1); }
        var review = await f.Store.ReviewRetentionApplyAsync(id, f.Token);
        var workspace = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        if (string.Equals(fault, "policy", StringComparison.Ordinal)) { policy.Activate(new(2, 5)); policy.Activate(new(1, 3)); }
        if (string.Equals(fault, "head", StringComparison.Ordinal)) { await workspace.CreateAsync(new("Unrelated source change"), RequestOrigin.LocalUi, f.Token); }
        if (string.Equals(fault, "generation", StringComparison.Ordinal)) { await workspace.ChangeLifecycleAsync(id, new(1), false, RequestOrigin.LocalUi, f.Token); }
        if (string.Equals(fault, "hold", StringComparison.Ordinal))
        {
            var observed = await f.Store.ReadRetentionObservationAsync(id, f.Token);
            await workspace.SetRetentionHoldAsync(observed, true, RequestOrigin.LocalUi, () => true, f.Token);
        }
        if (string.Equals(fault, "cutoff", StringComparison.Ordinal)) { f.Time.Now = review.ExpiresAt; }
        if (string.Equals(fault, "rollback-clock", StringComparison.Ordinal)) { f.Time.Now = review.Cutoff.AddTicks(-1); }
        if (string.Equals(fault, "due-drift", StringComparison.Ordinal)) { f.Time.Now = f.Time.Now.AddSeconds(1); }
        if (string.Equals(fault, "inventory", StringComparison.Ordinal))
        {
            var directory = new RestrictedStorageDirectory(f.Paths, includeKeys: false, partitionName: HostInteractionSchema.Partition);
            using var unknown = directory.CreateNewFile(Path.Combine(directory.Artifacts, "unknown-copy"));
            unknown.WriteByte(1);
        }
        if (string.Equals(fault, "source", StringComparison.Ordinal))
        {
            File.SetLastWriteTimeUtc(f.DatabasePath, File.GetLastWriteTimeUtc(f.DatabasePath).AddMinutes(1));
        }
        var audit = f.Count("security_audit_events");
        await f.Store.Invoking(store => store.ValidateRetentionApplyAsync(review, f.Token).AsTask())
            .Should().ThrowAsync<Exception>();
        (await f.Store.ReadRetentionAsync(id, f.Token)).ArchiveDue.Should().Be(before.ArchiveDue);
        f.Count("security_audit_events").Should().Be(audit);
    }

    [Theory]
    [InlineData("live")]
    [InlineData("unknown")]
    [InlineData("unavailable")]
    [InlineData("question")]
    [InlineData("perpetual")]
    public async Task IneligibleWorkAndKeptConversationsCannotBeReviewed(string fault)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        if (string.Equals(fault, "unknown", StringComparison.Ordinal)) { await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Unknown); }
        if (string.Equals(fault, "unavailable", StringComparison.Ordinal)) { await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Unavailable); }
        if (string.Equals(fault, "question", StringComparison.Ordinal))
        {
            await f.RunAsync(() => f.Questions.CreateAsync(f.Request, new("Unresolved",
                Kora.Core.Interaction.QuestionKind.Text, [], maximumTextLength: 32), f.Time.Now.AddMinutes(5), f.Token));
        }
        if (string.Equals(fault, "perpetual", StringComparison.Ordinal))
        {
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
            var workspace = WindowsSqliteSessionWorkspaceTests.Service(f, new());
            await workspace.SetRetentionHoldAsync(await f.Store.ReadRetentionObservationAsync(f.Request.SessionId, f.Token),
                true, RequestOrigin.LocalUi, () => true, f.Token);
        }
        await f.Store.Invoking(store => store.ReviewRetentionApplyAsync(f.Request.SessionId, f.Token).AsTask())
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("cancel")]
    [InlineData("admission")]
    [InlineData("copies-after-commit")]
    [InlineData("commit-unconfirmed")]
    public async Task FailurePreservesPrecommitStateOrTruthfullyHoldsKnownDeletion(string fault)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var policy = new SessionRetentionPolicy();
        policy.Activate(new(1, 3));
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint, sessionRetentionPolicy: policy);
        await f.Store.InitializeAsync(f.Token);
        var before = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
        f.Time.Now = before.LastMeaningfulActivity.AddDays(3);
        var review = await f.Store.ReviewRetentionApplyAsync(f.Request.SessionId, f.Token);
        using var cancel = new CancellationTokenSource();
        checkpoint.Audit = (_, _) => { if (string.Equals(fault, "audit", StringComparison.Ordinal)) { throw new IOException("Fixture audit unavailable"); } };
        checkpoint.Commit = (_, transaction) =>
        {
            if (string.Equals(fault, "cancel", StringComparison.Ordinal)) { cancel.Cancel(); }
            if (fault is "commit-unconfirmed")
            {
                transaction.Commit();
                throw new IOException("Fixture lost commit confirmation.");
            }
            if (string.Equals(fault, "copies-after-commit", StringComparison.Ordinal))
            {
                var directory = new RestrictedStorageDirectory(f.Paths, includeKeys: false, partitionName: HostInteractionSchema.Partition);
                using var unknown = directory.CreateNewFile(Path.Combine(directory.Artifacts, "unknown-copy"));
                unknown.WriteByte(1);
            }
        };
        var action = () => Apply(f, review, () => !string.Equals(fault, "admission", StringComparison.Ordinal), cancel.Token);
        await action.Should().ThrowAsync<Exception>();
        checkpoint.Commit = null;
        var actual = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
        actual.Should().Be(fault is "copies-after-commit" or "commit-unconfirmed" ? review.After : before);
        (await f.Store.ReadSessionAsync(f.Request.SessionId, f.Token))!.Generation.Value
            .Should().Be(fault is "copies-after-commit" or "commit-unconfirmed" ? 2 : 1);
        if (fault is "cancel")
        {
            (await f.Store.ReadRetentionObservationAsync(f.Request.SessionId, f.Token)).WorkHoldObserved.Should().BeFalse(
                "a cancelled retention control must not keep the conversation alive or renew meaningful activity");
        }
    }

    private static async Task<SessionRetentionApplyReceipt> Apply(InteractionStorageFixture f,
        SessionRetentionApplyReview review, Func<bool>? admitted = null, CancellationToken? token = null)
    {
        f.Request = new(new(Guid.NewGuid()), review.Before.State.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RecordControlIntentAsync(f.Request, f.Token);
        return await f.Store.ApplyReviewedRetentionAsync(f.Request, review, admitted ?? (() => true), token ?? f.Token);
    }
}
