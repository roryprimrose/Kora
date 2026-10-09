using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Communication;
using Kora.Core.Communication;
using Kora.Core.Hosting;
using Kora.Windows.Communication;
using Kora.Windows.Storage;
using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection("Host tracing")]
public sealed class WindowsSqliteManualCallControlTests
{
    [Fact]
    public async Task Real_private_sqlite_commits_original_intent_and_required_correlated_audits_on_one_lease_without_grant_or_question_effect()
    {
        using var f = new InteractionStorageFixture();
        await f.Tasks.InitializeAsync(f.Token);
        await f.Store.InitializeAsync(f.Token);
        await using var control = new ManualCallControl(f.Store, f.Store, new(f.Tasks));
        using var policy = new CallCommunicationPolicy(new UnavailableCallStateService());
        (await control.SetAsync(true, RequestOrigin.LocalUi, 0, policy, static () => true,
            static () => new(Task.CompletedTask, static () => true), f.Token)).Should().Be(CallMutationOutcome.Applied);
        (await control.SetAsync(false, RequestOrigin.LocalUi, 1, policy, static () => true,
            static () => new(Task.CompletedTask, static () => true), f.Token)).Should().Be(CallMutationOutcome.Applied);
        policy.Current.ManualActive.Should().BeFalse();
        policy.Current.AutomaticState.Should().Be(CallState.Unavailable);
        f.Count("host_questions").Should().Be(0);
        f.Count("scoped_grants").Should().Be(0);
        using var connection = f.OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT envelope FROM security_audit_events ORDER BY sequence;";
        using var rows = command.ExecuteReader();
        var entries = new List<JsonDocument>();
        try
        {
            while (rows.Read()) { entries.Add(JsonDocument.Parse(rows.GetString(0))); }
            entries.Should().HaveCount(5);
            var requested = entries[1].RootElement;
            var terminal = entries[2].RootElement;
            requested.GetProperty("Audit").GetProperty("ActionId").GetString().Should().Be("call.manual.on");
            requested.GetProperty("Audit").GetProperty("Outcome").GetInt32().Should().Be((int)Kora.Core.Auditing.SecurityAuditOutcome.Requested);
            terminal.GetProperty("Audit").GetProperty("Outcome").GetInt32().Should().Be((int)Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
            terminal.GetProperty("Audit").GetProperty("CorrelationId").GetString().Should().NotBe(requested.GetProperty("Audit").GetProperty("CorrelationId").GetString());
            terminal.GetProperty("Request").GetRawText().Should().Be(requested.GetProperty("Request").GetRawText());
            terminal.GetProperty("TraceId").GetString().Should().HaveLength(32);
            terminal.GetProperty("SpanId").GetString().Should().HaveLength(16);
        }
        finally { foreach (var entry in entries) { entry.Dispose(); } }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Required_requested_audit_failure_prevents_effect_and_terminal_failure_never_claims_memory_rollback(bool terminal)
    {
        using var f = new InteractionStorageFixture();
        await f.Tasks.InitializeAsync(f.Token);
        await f.Store.InitializeAsync(f.Token);
        var checkpoint = new Checkpoint(terminal ? 3 : 2);
        f.Reopen(checkpoint);
        await using var control = new ManualCallControl(f.Store, f.Store, new(f.Tasks));
        using var policy = new CallCommunicationPolicy(new UnavailableCallStateService());
        var run = () => control.SetAsync(true, RequestOrigin.LocalUi, 0, policy, static () => true,
            static () => new(Task.CompletedTask, static () => true), f.Token);
        await run.Should().ThrowAsync<IOException>();
        policy.Current.ManualActive.Should().Be(terminal);
        f.Count("security_audit_events").Should().Be(terminal ? 2 : 1);
        (await control.SetAsync(false, RequestOrigin.LocalUi, policy.Current.Revision, policy, static () => true,
            static () => new(Task.CompletedTask, static () => true), f.Token)).Should().Be(CallMutationOutcome.HostUnavailable);
    }

    private sealed class Checkpoint(int failure) : IHostInteractionTransactionCheckpoint
    {
        private int audits;
        public void BeforeAudit(SqliteConnection connection, SqliteTransaction transaction)
        {
            if (++audits == failure) { throw new IOException("required audit interrupted"); }
        }
        public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction) { }
    }

    [Theory]
    [InlineData(0, Kora.Core.Auditing.SecurityAuditOutcome.Succeeded)]
    [InlineData(1, Kora.Core.Auditing.SecurityAuditOutcome.Failed)]
    [InlineData(2, Kora.Core.Auditing.SecurityAuditOutcome.Cancelled)]
    public async Task Required_terminal_audit_waits_for_owned_resource_retirement_on_the_same_lease(
        int failure, Kora.Core.Auditing.SecurityAuditOutcome expected)
    {
        using var f = new InteractionStorageFixture();
        await f.Tasks.InitializeAsync(f.Token);
        await f.Store.InitializeAsync(f.Token);
        await using var control = new ManualCallControl(f.Store, f.Store, new(f.Tasks));
        using var policy = new CallCommunicationPolicy(new UnavailableCallStateService());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        policy.Changed += (_, _) =>
        {
            if (policy.Current.ManualActive) { published.TrySetResult(); }
        };
        var operation = control.SetAsync(true, RequestOrigin.LocalUi, 0, policy, static () => true,
            () => new(closure.Task, static () => true), cancellation.Token);
        await published.Task;
        operation.IsCompleted.Should().BeFalse();
        if (failure == 1) { closure.SetException(new IOException("unconfirmed resource closure")); }
        else
        {
            if (failure == 2) { await cancellation.CancelAsync(); }
            closure.SetResult();
        }
        if (failure == 0) { (await operation).Should().Be(CallMutationOutcome.Applied); }
        else
        {
            var failed = () => operation;
            await failed.Should().ThrowAsync<Exception>();
        }
        policy.Current.ManualActive.Should().BeTrue();
        policy.Current.ManualControlEvidenceUnavailable.Should().Be(failure != 0);
        f.Count("security_audit_events").Should().Be(3);
        using var connection = f.OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT envelope FROM security_audit_events ORDER BY sequence DESC LIMIT 1;";
        using var terminal = JsonDocument.Parse((string)command.ExecuteScalar()!);
        terminal.RootElement.GetProperty("Audit").GetProperty("Outcome").GetInt32().Should().Be((int)expected);
    }

    [Fact]
    public async Task Real_manual_control_does_not_edit_independent_perpetual_records_or_pending_exact_questions()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        f.Request = InteractionStorageFixture.NewRequest(f.Request.SessionId);
        await f.AdmitAsync(newSession: false);
        var question = await f.PresentAsync();
        var questions = await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token);
        var grants = await f.Store.ReadGrantsAsync(f.Token);
        grants.Should().Contain(record => record.Id == grant.Id
            && record.Status == Kora.Core.Authorization.OperationGrantStatus.Active);
        await using var control = new ManualCallControl(f.Store, f.Store, new(f.Tasks));
        using var policy = new CallCommunicationPolicy(new UnavailableCallStateService());
        await control.SetAsync(true, RequestOrigin.LocalUi, 0, policy, static () => true,
            static () => new(Task.CompletedTask, static () => true), f.Token);
        await control.SetAsync(false, RequestOrigin.LocalUi, 1, policy, static () => true,
            static () => new(Task.CompletedTask, static () => true), f.Token);
        (await f.Store.ReadGrantsAsync(f.Token)).Should().Equal(grants);
        HostInteractionCodec.Encode(await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token))
            .Should().Be(HostInteractionCodec.Encode(questions));
        grants.Should().Contain(grant);
        questions.Should().Contain(record => record.Key == question.Key);
        policy.Current.AutomaticState.Should().Be(CallState.Unavailable);
    }

    [Fact]
    public async Task Real_terminal_audit_failure_after_off_keeps_the_truthful_off_flag_but_conservative_protection_held()
    {
        using var f = new InteractionStorageFixture();
        await f.Tasks.InitializeAsync(f.Token);
        await f.Store.InitializeAsync(f.Token);
        f.Reopen(new Checkpoint(5));
        await using var control = new ManualCallControl(f.Store, f.Store, new(f.Tasks));
        using var policy = new CallCommunicationPolicy(new UnavailableCallStateService());
        await control.SetAsync(true, RequestOrigin.LocalUi, 0, policy, static () => true,
            static () => new(Task.CompletedTask, static () => true), f.Token);
        var off = () => control.SetAsync(false, RequestOrigin.LocalUi, 1, policy, static () => true,
            static () => new(Task.CompletedTask, static () => true), f.Token);
        await off.Should().ThrowAsync<IOException>();
        policy.Current.ManualActive.Should().BeFalse();
        policy.Current.AutomaticState.Should().Be(CallState.Unavailable);
        policy.Current.ManualControlEvidenceUnavailable.Should().BeTrue();
        policy.Current.SuppressSpeech.Should().BeTrue();
        policy.Current.AllowActivation.Should().BeFalse();
        policy.Current.Authorization(true, true).AllowsReusableGrants.Should().BeFalse();
        f.Count("security_audit_events").Should().Be(4);
    }

    [Fact]
    public async Task Real_ended_session_generation_and_missing_intent_cannot_release_manual_protection()
    {
        using var f = new InteractionStorageFixture();
        await f.Tasks.InitializeAsync(f.Token);
        await f.Store.InitializeAsync(f.Token);
        await using var control = new ManualCallControl(f.Store, f.Store, new(f.Tasks));
        using var policy = new CallCommunicationPolicy(new UnavailableCallStateService());
        await control.SetAsync(true, RequestOrigin.LocalUi, 0, policy, static () => true,
            static () => new(Task.CompletedTask, static () => true), f.Token);
        var session = (await f.Store.ReadSessionsAsync(null, 10, f.Token)).Records.Single();
        f.Request = InteractionStorageFixture.NewRequest(session.SessionId);
        var missingIntent = () => f.RunAsync(() => f.Store.ApplyManualCallAsync(f.Request, session.Generation, false,
            static () => true, static () => throw new InvalidOperationException("Must not run"), f.Token));
        await missingIntent.Should().ThrowAsync<InvalidDataException>();
        await f.RunAsync(async () => await f.Store.RecordControlIntentAsync(f.Request, f.Token));
        await f.RunAsync(() => f.Store.ChangeIdleLifecycleAsync(f.Request, session.Generation, false, static () => true, f.Token));
        var ended = () => control.SetAsync(false, RequestOrigin.LocalUi, policy.Current.Revision, policy,
            static () => true, static () => new(Task.CompletedTask, static () => true), f.Token);
        await ended.Should().ThrowAsync<InvalidOperationException>();
        policy.Current.ManualActive.Should().BeTrue();
        policy.Current.ManualControlEvidenceUnavailable.Should().BeTrue();
        policy.Current.AutomaticState.Should().Be(CallState.Unavailable);
    }
}
