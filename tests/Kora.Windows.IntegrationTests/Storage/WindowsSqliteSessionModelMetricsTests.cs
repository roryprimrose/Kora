using System.Text;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed class WindowsSqliteSessionModelMetricsTests
{
    [Fact]
    public async Task OriginalMultiModelObservationsSurviveRestartAndRemainSessionIsolatedWithoutActivityRenewal()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var firstSession = f.Request.SessionId;
        var first = Usage(f, "first-model");
        var second = Usage(f, "second-model") with
        {
            Provenance = first.Provenance with
            { Turn = new(Guid.NewGuid()), Selection = ModelProviderSelection.CopilotCandidate },
            TokenUsage = null,
            ObservedAt = first.ObservedAt.AddSeconds(1),
        };
        await Record(f, second);
        await Record(f, first);
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        await WindowsSqliteConversationTests.FinishInitial(f);
        var unrelated = Usage(f, "unrelated-model") with { TokenUsage = new(long.MaxValue, null, null, null) };
        await Record(f, unrelated);
        var retention = await f.Store.ReadRetentionAsync(firstSession, f.Token);
        var audits = WindowsSqliteConversationTests.AuditText(f);
        var tasks = f.Count("host_task_events");
        f.Time.Now = f.Time.Now.AddHours(2);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        ISessionModelMetricsStore store = f.Store;
        (await store.ReadModelUsageAsync(firstSession, f.Token)).Should().Equal(first, second);
        (await store.ReadModelUsageAsync(unrelated.Provenance.Request.SessionId, f.Token)).Should().Equal(unrelated);
        (await f.Store.ReadRetentionAsync(firstSession, f.Token)).Should().Be(retention);
        WindowsSqliteConversationTests.AuditText(f).Should().Be(audits);
        f.Count("host_task_events").Should().Be(tasks);
    }

    [Fact]
    public async Task ExactTurnReplayIsIdempotentAcrossRestartAndConflictingUsageCannotOverwriteOriginal()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var usage = Usage(f, "original-model");
        await Record(f, usage);
        await Record(f, usage);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        await Record(f, usage);
        var conflict = () => Record(f, usage with { Model = "conflicting-model" });
        await conflict.Should().ThrowAsync<InvalidDataException>();
        var changedCount = () => Record(f, usage with { TokenUsage = new(101, 2, 103, 1000) });
        await changedCount.Should().ThrowAsync<InvalidDataException>();
        f.Count("session_model_usage").Should().Be(1);
        (await f.Store.ReadModelUsageAsync(f.Request.SessionId, f.Token)).Should().Equal(usage);
    }

    [Fact]
    public async Task RetainedConversationExecutionAndHostOwnedFollowOnTurnsShareOnlyTheirOriginalRequestGeneration()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var message = await WindowsSqliteAnswerReviewTests.Answer(f, f.Request.SessionId, "source");
        var usage = Usage(f, "conversation-model") with { Provenance = message.Execution! };
        var followOn = usage with
        {
            Provenance = usage.Provenance with { Turn = new(Guid.NewGuid()), Selection = ModelProviderSelection.CopilotCandidate },
            Model = "host-selected-follow-on",
            ObservedAt = usage.ObservedAt.AddSeconds(1),
        };
        await Record(f, usage);
        await Record(f, followOn);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await f.Store.ReadModelUsageAsync(f.Request.SessionId, f.Token)).Should().Equal(usage, followOn);
        (await f.Store.ReadConversationMessageAsync(f.Request.SessionId, message.Request.RequestId, f.Token)).Should().Be(message);
    }

    [Fact]
    public async Task CapturedLateCompletionRetainsOriginalGenerationAfterDoneWithoutRenewalOrNewAuthority()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var usage = Usage(f, "late-model");
        Func<HostActivity> continuation;
        using (var original = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Runtime))
        {
            continuation = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Storage);
        }
        var control = WindowsSqliteConversationTests.Fresh(f.Request.SessionId);
        using (var root = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RecordControlIntentAsync(control, f.Token);
            await f.Store.SetSessionLifecycleAsync(control, new(1), active: false, remove: false, f.Token);
        }
        var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
        var audit = WindowsSqliteConversationTests.AuditText(f);
        f.Time.Now = f.Time.Now.AddHours(1);
        using (var captured = continuation())
        {
            await f.Store.RecordModelUsageAsync(usage, CancellationToken.None);
        }
        (await f.Store.ReadModelUsageAsync(f.Request.SessionId, f.Token)).Should().Equal(usage);
        (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
        WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
        using var connection = f.OpenRaw();
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT generation FROM session_model_usage;";
        read.ExecuteScalar().Should().Be(1L);
    }

    [Theory]
    [InlineData("no-context")]
    [InlineData("wrong-context")]
    [InlineData("stopped-context")]
    [InlineData("missing-task")]
    [InlineData("cross-session")]
    [InlineData("missing-generation")]
    public async Task MissingOrCrossBoundHostAuthorityNeverPersistsUsage(string fault)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var usage = Usage(f, "refused-model");
        if (fault is "missing-task")
        {
            usage = usage with { Provenance = usage.Provenance with { Request = WindowsSqliteConversationTests.Fresh(f.Request.SessionId) } };
        }
        if (fault is "cross-session")
        {
            var original = usage.Provenance.Request;
            usage = usage with { Provenance = usage.Provenance with
            { Request = new(original.RequestId, new(Guid.NewGuid()), original.TaskId, original.Origin, original.InvocationId) } };
        }
        if (fault is "missing-generation")
        {
            var request = WindowsSqliteConversationTests.Fresh(f.Request.SessionId);
            using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            await f.Store.RecordControlIntentAsync(request, f.Token);
            usage = usage with { Provenance = usage.Provenance with { Request = request } };
        }
        using var host = fault is "no-context" ? null : HostActivity.BeginRoot(
            fault is "wrong-context" ? HostRequest.Create(RequestOrigin.HostSystem) : usage.Provenance.Request,
            HostActivityLayer.Application, HostOperation.Storage);
        if (fault is "stopped-context") { host!.Activity!.Stop(); }
        var operation = () => f.Store.RecordModelUsageAsync(usage, f.Token).AsTask();
        await operation.Should().ThrowAsync<Exception>();
        f.Count("session_model_usage").Should().Be(0);
    }

    [Theory]
    [InlineData("disposition")]
    [InlineData("retention")]
    public async Task SessionRemovalPurgesUsageAndRejectsLateRestorationWhileRetainingOtherSessions(string path)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var usage = Usage(f, "REMOVED_MODEL_USAGE_51F84");
        var target = f.Request.SessionId;
        await Record(f, usage);
        f.Time.Now = (await f.Store.ReadRetentionAsync(target, f.Token)).DeleteDue;
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        await WindowsSqliteConversationTests.FinishInitial(f);
        var unrelated = Usage(f, "retained-model");
        await Record(f, unrelated);
        if (path is "disposition")
        {
            var preview = await f.Store.PreviewDispositionAsync(target, new(1), 0, f.Token);
            var control = WindowsSqliteConversationTests.Fresh(target);
            using var root = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request);
            await f.Store.RecordControlIntentAsync(control, f.Token);
            await f.Store.DisposeSessionAsync(control, preview, static () => true, f.Token);
        }
        else
        {
            using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
                HostActivityLayer.Application, HostOperation.Retention);
            (await f.Store.ApplyRetentionAsync(static () => true, static (_, _) => ValueTask.CompletedTask, f.Token))
                .Deleted.Should().Be(1);
        }
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await f.Store.ReadModelUsageAsync(target, f.Token)).Should().BeEmpty();
        (await f.Store.ReadModelUsageAsync(unrelated.Provenance.Request.SessionId, f.Token)).Should().Equal(unrelated);
        var late = () => Record(f, usage);
        await late.Should().ThrowAsync<InvalidDataException>();
        f.Count("session_model_usage").Should().Be(1);
        Encoding.UTF8.GetString(File.ReadAllBytes(f.DatabasePath)).Should().NotContain("REMOVED_MODEL_USAGE_51F84");
        new FileInfo(f.DatabasePath + "-journal").Length.Should().Be(0);
    }

    [Theory]
    [InlineData("UPDATE session_model_usage SET payload=json_set(payload,'$.TokenUsage.InputTokens',-1);")]
    [InlineData("UPDATE session_model_usage SET payload=json_set(payload,'$.Model','substituted-model');")]
    [InlineData("UPDATE session_model_usage SET payload='{}';")]
    [InlineData("UPDATE session_model_usage SET generation=generation+1;")]
    [InlineData("UPDATE session_model_usage SET audit_sequence=audit_sequence+1;")]
    [InlineData("UPDATE session_model_usage SET request_id='11111111-1111-1111-1111-111111111111';")]
    public async Task CorruptOrCrossBoundStoredObservationFailsExplicitlyBeforeReturningMetrics(string mutation)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await Record(f, Usage(f, "corruption-target"));
        f.Mutate(mutation);
        f.Reopen();
        await f.Store.Invoking(store => store.InitializeAsync(f.Token).AsTask()).Should().ThrowAsync<InvalidDataException>();
        await f.Store.Invoking(store => store.ReadModelUsageAsync(f.Request.SessionId, f.Token).AsTask())
            .Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SchemaFourteenMigrationPreservesHistoryAuditAndRetentionWithoutFabricatedUsage(bool interrupted)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var audits = WindowsSqliteConversationTests.AuditText(f);
        var history = await f.Store.ReadHistoryAsync(f.Request.SessionId, null, 50, f.Token);
        var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
        f.DropEmptyModelUsageSchemaForLegacy();
        f.Mutate("PRAGMA user_version=14;");
        if (interrupted)
        {
            var checkpoint = new InteractionTransactionCheckpoint
            { Commit = static (_, _) => throw new IOException("migration interrupted") };
            f.Reopen(checkpoint);
            await f.Store.Invoking(store => store.InitializeAsync(f.Token).AsTask()).Should().ThrowAsync<IOException>();
            using var connection = f.OpenRaw();
            using var read = connection.CreateCommand();
            read.CommandText = "PRAGMA user_version;";
            read.ExecuteScalar().Should().Be(14L);
        }
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await f.Store.ReadModelUsageAsync(f.Request.SessionId, f.Token)).Should().BeEmpty();
        WindowsSqliteConversationTests.AuditText(f).Should().Be(audits);
        (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
        (await f.Store.ReadHistoryAsync(f.Request.SessionId, null, 50, f.Token)).Should().BeEquivalentTo(history);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("host-ended")]
    [InlineData("commit-failure")]
    public async Task FinalCommitBoundaryFailuresRollBackWithoutAUsageReceipt(string failure)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Storage);
        checkpoint.Commit = (_, _) =>
        {
            if (failure is "cancel") { cancellation.Cancel(); }
            if (failure is "host-ended") { root.Activity!.Stop(); }
            if (failure is "commit-failure") { throw new IOException("commit refused"); }
        };
        var operation = () => f.Store.RecordModelUsageAsync(Usage(f, "uncommitted"), cancellation.Token).AsTask();
        await operation.Should().ThrowAsync<Exception>();
        f.Count("session_model_usage").Should().Be(0);
    }

    private static SessionModelUsage Usage(InteractionStorageFixture f, string model) => new(
        new(new(Guid.NewGuid()), f.Request, ModelProviderSelection.OllamaCandidate),
        model, new(100, 2, 102, 1000), TimeSpan.FromMilliseconds(125), f.Time.Now);

    private static async Task Record(InteractionStorageFixture f, SessionModelUsage usage)
    {
        using var root = HostActivity.BeginRoot(usage.Provenance.Request, HostActivityLayer.Application, HostOperation.Storage);
        await f.Store.RecordModelUsageAsync(usage, f.Token);
    }
}
