using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionPermissionTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task Reviewed_permission_command_uses_real_transaction_preserves_grants_and_retires_old_cards(RequestOrigin origin)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        var created = await f.RunAsync(() => f.Questions.CreateAsync(f.Request,
            new("Pending clarification", QuestionKind.Text, [], maximumTextLength: 32), f.Time.Now.AddMinutes(5), f.Token));
        created.Outcome.Should().Be(HostInteractionOutcome.Presented);
        var question = created.Question!;
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var workspace = WindowsSqliteSessionWorkspaceTests.Service(f, access);
        var commands = new SessionPermissionCommandService(workspace, access, f.Time);
        var before = await f.Store.ReadSessionPermissionAsync(f.Request.SessionId, f.Token);
        var audit = f.Count("security_audit_events");
        var id = f.Request.SessionId.Value;
        var reviewed = await commands.ExecuteAsync(new(SessionCommandOperation.PermissionReview, id)
        { PermissionLevel = SessionPermissionLevel.ApproveAll }, origin, () => true, f.Token);
        reviewed.Outcome.Should().Be("review");
        f.Count("security_audit_events").Should().Be(audit);
        (await f.Store.ReadSessionPermissionAsync(f.Request.SessionId, f.Token)).Should().Be(before);
        var result = await commands.ExecuteAsync(new(SessionCommandOperation.PermissionConfirm, id), origin, () => true, f.Token);
        result.Outcome.Should().Be("committed");
        var policy = await f.Store.ReadSessionPermissionAsync(f.Request.SessionId, f.Token);
        policy.Level.Should().Be(SessionPermissionLevel.ApproveAll);
        policy.Revision.Value.Should().Be(before.Revision.Value + 1);
        (await f.Store.ReadGrantsAsync(f.Token)).Should().ContainSingle().Which.Should().Be(grant);
        (await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token))
            .Single(item => item.Key.QuestionId == question.Key.QuestionId).Status.Should().Be(QuestionStatus.Cancelled);
        var tasks = await f.Store.ReadTaskPageAsync(f.Request.SessionId, null, 50, f.Token);
        var control = tasks.Records.Single(task => task.Request.TaskId != f.Request.TaskId);
        control.State.Should().Be(HostTaskState.Succeeded);
        control.Request.Origin.Should().Be(origin);
        control.Request.InvocationId.Should().BeNull();
        f.Reopen();
        (await f.Store.ReadSessionPermissionAsync(f.Request.SessionId, f.Token)).Should().Be(policy);
        await FluentActions.Awaiting(() => commands.ExecuteAsync(new(SessionCommandOperation.PermissionConfirm, id), origin,
            () => true, f.Token)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("authority")]
    [InlineData("audit")]
    public async Task Voice_permission_commit_rechecks_authority_and_audit_before_any_policy_or_question_change(string failure)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var question = await f.PresentAsync();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var before = await f.Store.ReadSessionPermissionAsync(f.Request.SessionId, f.Token);
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint);
        var workspace = WindowsSqliteSessionWorkspaceTests.Service(f, access);
        var commands = new SessionPermissionCommandService(workspace, access, f.Time);
        var id = f.Request.SessionId.Value;
        await commands.ExecuteAsync(new(SessionCommandOperation.PermissionReview, id)
        { PermissionLevel = SessionPermissionLevel.ApproveAll }, RequestOrigin.ActivatedVoice, () => true, f.Token);
        if (failure is "authority") { checkpoint.Commit = (_, _) => access.ControlRevision++; }
        else { checkpoint.Audit = (_, _) => throw new IOException("Required audit unavailable"); }
        await FluentActions.Awaiting(() => commands.ExecuteAsync(new(SessionCommandOperation.PermissionConfirm, id),
            RequestOrigin.ActivatedVoice, () => true, f.Token)).Should().ThrowAsync<Exception>();
        (await f.Store.ReadSessionPermissionAsync(f.Request.SessionId, f.Token)).Should().Be(before);
        (await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token))
            .Single(item => item.Key.QuestionId == question.Key.QuestionId).Should().BeEquivalentTo(question);
        (await f.Store.ReadTaskPageAsync(f.Request.SessionId, null, 50, f.Token)).Records
            .Should().NotContain(task => task.State == HostTaskState.Succeeded);
    }
}
