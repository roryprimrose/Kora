using AwesomeAssertions;
using Kora.Application.Diagnostics;
using Kora.Application.Communication;
using Kora.Core.Communication;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection("Host tracing")]
public sealed class SessionPermissionTests
{
    [WindowsFact]
    public async Task Live_call_policy_is_checked_inside_answer_commit_not_the_old_presentation_snapshot()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var presented = await f.PresentAsync();
        using var calls = new CallCommunicationPolicy(new ProtectedCall());
        f.Store.BindAuthorizationPolicyGate(calls);
        FluentActions.Invoking(() => f.Store.BindAuthorizationPolicyGate(calls)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => f.Store.BindAuthorizationPolicyGate(null!)).Should().Throw<ArgumentNullException>();
        var denied = await f.RunAsync(() => f.Authorization.ApproveAsync(presented.Key, new(["session"]),
            RequestOrigin.ActivatedVoice, f.Token));
        denied.Reason.Should().Be("approval-channel-unavailable-use-native-ui");
        (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
        (await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token)).Single().Status.Should().Be(QuestionStatus.Pending);
        var ui = await f.RunAsync(() => f.Authorization.ApproveAsync(presented.Key, new(["session"]), RequestOrigin.LocalUi, f.Token));
        ui.Outcome.Should().Be(HostInteractionOutcome.Approved);
        var used = await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, ui.Grant!.Id, ui.Grant.Revision, f.Token));
        used.Outcome.Should().Be(HostInteractionOutcome.Consumed);
    }
    [Theory]
    [InlineData(SessionPermissionLevel.ReviewAll)]
    [InlineData(SessionPermissionLevel.ModelReview)]
    [InlineData(SessionPermissionLevel.ApproveAll)]
    public async Task Exact_active_policy_survives_restart_and_done_resume_never_revives_it(SessionPermissionLevel level)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var initial = await f.Store.ReadSessionPermissionAsync(f.Request.SessionId, f.Token);
        initial.Level.Should().Be(SessionPermissionLevel.ReviewAll);
        var grant = await f.GrantAsync("perpetual");
        var saved = await Set(f, initial, level);
        saved.Revision.Value.Should().Be(2);
        (await f.Store.ReadGrantsAsync(f.Token)).Should().ContainSingle().Which.Should().Be(grant);
        f.Reopen();
        (await f.Store.ReadSessionPermissionAsync(f.Request.SessionId, f.Token)).Should().Be(saved);
        var done = await f.RunAsync(() => f.Store.SetSessionLifecycleAsync(f.Request, new(1), false, false, f.Token));
        var reset = await f.Store.ReadSessionPermissionAsync(f.Request.SessionId, f.Token);
        reset.Level.Should().Be(SessionPermissionLevel.ReviewAll);
        reset.Generation.Should().Be(done.Generation);
        f.Reopen();
        var resumed = await f.RunAsync(() => f.Store.SetSessionLifecycleAsync(f.Request, done.Generation, true, false, f.Token));
        var current = await f.Store.ReadSessionPermissionAsync(f.Request.SessionId, f.Token);
        current.Should().Be(SessionPermissionPolicy.Default(resumed));
        (await f.Store.ReadGrantsAsync(f.Token)).Should().ContainSingle().Which.Should().Be(grant);
        f.Count("session_admissions").Should().Be(0);
    }

    [WindowsFact]
    public async Task Policy_change_retires_questions_and_undispatched_admissions_without_touching_grants()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var policy = await Set(f, await f.Store.ReadSessionPermissionAsync(f.Request.SessionId, f.Token),
            SessionPermissionLevel.ApproveAll);
        await f.PublishAsyncAfterLifecycle();
        var admitted = await f.RunAsync(() => f.Authorization.AuthorizeAsync(f.Request, false, f.Token));
        admitted.Admission.Should().NotBeNull();
        var question = await f.PresentAsync();
        var next = await Set(f, policy, SessionPermissionLevel.ReviewAll);
        next.Revision.Value.Should().Be(3);
        f.Count("session_admissions").Should().Be(0);
        (await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token))
            .Single(item => item.Key.QuestionId == question.Key.QuestionId).Status.Should().Be(QuestionStatus.Cancelled);
        await f.PublishAsyncAfterLifecycle();
        (await f.RunAsync(() => f.Authorization.DispatchAsync(f.Request, admitted.Admission!, f.Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Conflict);
        (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
    }

    [WindowsFact]
    public async Task Committed_receipts_are_exact_and_single_use_and_restart_does_not_restore_dispatch_authority()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await Set(f, await f.Store.ReadSessionPermissionAsync(f.Request.SessionId, f.Token), SessionPermissionLevel.ApproveAll);
        await f.PublishAsyncAfterLifecycle();
        var admitted = await f.RunAsync(() => f.Authorization.AuthorizeAsync(f.Request, false, f.Token));
        var receipt = admitted.Admission!;
        var dispatched = await f.RunAsync(() => f.Authorization.DispatchAsync(f.Request, receipt, f.Token));
        dispatched.Outcome.Should().Be(HostInteractionOutcome.Consumed);
        (await f.RunAsync(() => f.Authorization.DispatchAsync(f.Request, receipt, f.Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Conflict);
        (await f.RunAsync(() => f.Authorization.ValidateAdmissionAsync(f.Request, dispatched.Admission!, f.Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Approved);
        f.Reopen();
        await f.PublishAsync();
        (await f.RunAsync(() => f.Authorization.ValidateAdmissionAsync(f.Request, dispatched.Admission!, f.Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Denied);
    }

    [Theory]
    [InlineData("DELETE FROM session_permission;")]
    [InlineData("UPDATE session_permission SET level=0;")]
    [InlineData("UPDATE session_permission SET generation=2;")]
    [InlineData("UPDATE session_permission SET revision=3;")]
    public async Task Corrupt_or_deleted_policy_is_not_reinterpreted_as_a_default(string corruption)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await Set(f, await f.Store.ReadSessionPermissionAsync(f.Request.SessionId, f.Token), SessionPermissionLevel.ApproveAll);
        f.Mutate(corruption);
        await FluentActions.Awaiting(async () => await f.Store.ReadSessionPermissionAsync(f.Request.SessionId, f.Token))
            .Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task Schema_twelve_migrates_explicitly_to_review_all_without_replaying_work()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        f.DropEmptySessionPermissionSchemaForLegacy();
        f.Mutate("PRAGMA user_version=12;");
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        var policy = await f.Store.ReadSessionPermissionAsync(f.Request.SessionId, f.Token);
        policy.Level.Should().Be(SessionPermissionLevel.ReviewAll);
        policy.Revision.Value.Should().Be(1);
        f.Count("session_admissions").Should().Be(0);
        (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_fixture_staging_never_discards_current_permission_or_admission_authority(bool admission)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        if (admission)
        {
            var grant = await f.GrantAsync("once");
            var used = await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, f.Token));
            used.Outcome.Should().Be(HostInteractionOutcome.Consumed);
            f.Count("session_admissions").Should().Be(1);
        }
        else
        {
            await Set(f, await f.Store.ReadSessionPermissionAsync(f.Request.SessionId, f.Token), SessionPermissionLevel.ApproveAll);
            f.Count("session_permission").Should().Be(1);
        }
        var before = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f.DatabasePath));
        FluentActions.Invoking(f.DropEmptySessionPermissionSchemaForLegacy).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(f.DropEmptyConversationSchemaForLegacy).Should().Throw<InvalidOperationException>();
        System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f.DatabasePath)).Should().Equal(before);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        f.Count("session_admissions").Should().Be(admission ? 1 : 0);
        f.Count("session_permission").Should().Be(admission ? 0 : 1);
    }

    [WindowsFact]
    public async Task Fresh_native_review_and_separate_confirmation_change_only_the_exact_selected_session()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var first = f.Request.SessionId;
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        var second = f.Request.SessionId;
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var model = new SessionsViewModel(WindowsSqliteSessionWorkspaceTests.Service(f, access),
            new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance);
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single(entry => entry.Authority.SessionId == first));
        await model.ReadPermissionAsync();
        model.PermissionStatus.Should().Contain("Review all").And.Contain(first.Value.ToString("D"));
        model.PermissionChoice = 2;
        model.CanConfirmPermission.Should().BeFalse();
        model.ReviewPermission();
        model.CanConfirmPermission.Should().BeTrue();
        model.PermissionStatus.Should().Contain(SessionPermissionPolicy.ApproveAllWarning);
        (await f.Store.ReadSessionPermissionAsync(first, f.Token)).Level.Should().Be(SessionPermissionLevel.ReviewAll);
        await model.ConfirmPermissionAsync();
        model.Status.Should().NotContain("unavailable/denied");
        (await f.Store.ReadSessionPermissionAsync(first, f.Token)).Level.Should().Be(SessionPermissionLevel.ApproveAll);
        (await f.Store.ReadSessionPermissionAsync(second, f.Token)).Level.Should().Be(SessionPermissionLevel.ReviewAll);
        model.CanConfirmPermission.Should().BeFalse();
        model.ReviewPermission();
        model.CancelPermissionReview();
        model.CanConfirmPermission.Should().BeFalse();
        await model.SelectAsync(model.Sessions.Single(entry => entry.Authority.SessionId == second));
        model.PermissionStatus.Should().Contain("Review all").And.Contain(second.Value.ToString("D"));
        model.Close();
        model.PermissionStatus.Should().NotContain(first.Value.ToString("D"));
    }

    [Theory]
    [InlineData(RequestOrigin.ActivatedVoice)]
    [InlineData(RequestOrigin.HostSystem)]
    public async Task Ambient_origin_cannot_be_relabelled_as_a_native_control(RequestOrigin origin)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var workspace = WindowsSqliteSessionWorkspaceTests.Service(f, access);
        var before = await f.Store.ReadSessionPermissionAsync(f.Request.SessionId, f.Token);
        using var ambient = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Tool);
        await FluentActions.Awaiting(() => workspace.SetSessionPermissionAsync(before, SessionPermissionLevel.ApproveAll,
            static () => true, f.Token)).Should().ThrowAsync<InvalidOperationException>();
        (await f.Store.ReadSessionPermissionAsync(f.Request.SessionId, f.Token)).Should().Be(before);
    }

    [WindowsFact]
    public async Task Stale_cross_session_and_lost_private_admission_cannot_change_policy()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var before = await f.Store.ReadSessionPermissionAsync(f.Request.SessionId, f.Token);
        await FluentActions.Awaiting(() => Set(f, before with { Revision = new(2) }, SessionPermissionLevel.ApproveAll))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => Set(f, before with { SessionId = new(Guid.NewGuid()) }, SessionPermissionLevel.ApproveAll))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => Set(f, before, SessionPermissionLevel.ApproveAll, static () => false))
            .Should().ThrowAsync<InvalidOperationException>();
        (await f.Store.ReadSessionPermissionAsync(f.Request.SessionId, f.Token)).Should().Be(before);
    }

    private static async Task<SessionPermissionPolicy> Set(InteractionStorageFixture f, SessionPermissionPolicy expected,
        SessionPermissionLevel level, Func<bool>? admitted = null)
    {
        var previous = f.Request;
        f.Request = new(new(Guid.NewGuid()), previous.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        try
        {
            return await f.RunAsync(async () =>
            {
                await f.Store.RecordControlIntentAsync(f.Request, f.Token);
                return await f.Store.SetSessionPermissionAsync(f.Request, expected, level, admitted ?? (static () => true), f.Token);
            });
        }
        finally { f.Request = previous; }
    }

    private sealed class ProtectedCall : ICallStateService
    {
        public CallState CurrentState => CallState.Active;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged { add { } remove { } }
    }
}
