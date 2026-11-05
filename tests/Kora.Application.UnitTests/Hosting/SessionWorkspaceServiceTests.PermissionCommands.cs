using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, SessionPermissionLevel.ReviewAll)]
    [InlineData(RequestOrigin.ActivatedVoice, SessionPermissionLevel.ModelReview)]
    [InlineData(RequestOrigin.ActivatedVoice, SessionPermissionLevel.ApproveAll)]
    public async Task Permission_command_completes_exact_review_and_original_channel_commit_once(
        RequestOrigin origin, SessionPermissionLevel desired)
    {
        using var f = new PermissionFixture();
        var clock = new RetentionCommandTime();
        var commands = new SessionPermissionCommandService(f.Service, f, clock);
        var id = f.Session.SessionId.Value;
        var observed = await commands.ExecuteAsync(new(SessionCommandOperation.PermissionStatus, id), origin, () => true, f.Token);
        observed.Outcome.Should().Be("observed");
        observed.Message.Should().Contain("Review all").And.Contain("Unnamed").And.Contain(id.ToString("D"));
        var review = await commands.ExecuteAsync(new(SessionCommandOperation.PermissionReview, id) { PermissionLevel = desired },
            origin, () => true, f.Token);
        review.Outcome.Should().Be("review");
        review.Message.Should().Contain("60 seconds").And.Contain("Original channel: " + origin);
        if (desired == SessionPermissionLevel.ApproveAll) { review.Message.Should().Contain(SessionPermissionPolicy.ApproveAllWarning); }
        f.TaskWrites.Should().BeEmpty();
        f.Saved.Should().BeNull();
        clock.Now = clock.Now.AddSeconds(59);
        var confirm = new SessionCommand(SessionCommandOperation.PermissionConfirm, id);
        (await commands.ExecuteAsync(confirm, origin, () => true, f.Token)).Outcome.Should().Be("committed");
        f.Saved!.Level.Should().Be(desired);
        f.PermissionWrites.Should().Be(1);
        f.TaskWrites.Should().HaveCount(2).And.OnlyContain(task => task.Request.SessionId == f.Session.SessionId
            && task.Request.Origin == origin && task.Request.InvocationId == null);
        f.TaskWrites.Last().State.Should().Be(HostTaskState.Succeeded);
        await FluentActions.Awaiting(() => commands.ExecuteAsync(confirm, origin, () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.PermissionWrites.Should().Be(1);
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("origin")]
    [InlineData("session")]
    [InlineData("source")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("revision")]
    [InlineData("retired")]
    [InlineData("policy")]
    [InlineData("generation")]
    [InlineData("metadata")]
    [InlineData("late-source")]
    [InlineData("late-retire")]
    [InlineData("commit-source")]
    [InlineData("commit-revision")]
    public async Task Permission_confirmation_refuses_stale_revoked_cross_channel_and_commit_races(string failure)
    {
        using var f = new PermissionFixture();
        var clock = new RetentionCommandTime();
        var commands = new SessionPermissionCommandService(f.Service, f, clock);
        var id = f.Session.SessionId.Value;
        var source = true;
        await commands.ExecuteAsync(new(SessionCommandOperation.PermissionReview, id) { PermissionLevel = SessionPermissionLevel.ApproveAll },
            RequestOrigin.ActivatedVoice, () => source, f.Token);
        if (failure is "expiry") { clock.Now = clock.Now.AddSeconds(60); }
        if (failure is "source") { source = false; }
        if (failure is "private") { f.CanInspect = false; }
        if (failure is "control") { f.CanControl = false; }
        if (failure is "revision") { f.AdvanceRevision(); }
        if (failure is "retired") { commands.Retire(); }
        if (failure is "policy") { f.Saved = SessionPermissionPolicy.Default(f.Session) with { Revision = new(2) }; }
        if (failure is "generation") { f.Session = f.Session with { Generation = new(2) }; }
        if (failure is "metadata")
        { f.ExactListReader = _ => new(f.Session, new(f.Session.SessionId, new(1), new("Changed"))); }
        if (failure is "late-source") { f.ExactListReader = _ => { source = false; return new(f.Session, null); }; }
        if (failure is "late-retire") { f.ExactListReader = _ => { commands.Retire(); return new(f.Session, null); }; }
        if (failure is "commit-source") { f.BeforePermissionWrite = () => source = false; }
        if (failure is "commit-revision") { f.BeforePermissionWrite = f.AdvanceRevision; }
        var origin = failure is "origin" ? RequestOrigin.LocalUi : RequestOrigin.ActivatedVoice;
        var command = new SessionCommand(SessionCommandOperation.PermissionConfirm, failure is "session" ? Guid.NewGuid() : id);
        await FluentActions.Awaiting(() => commands.ExecuteAsync(command, origin, () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.PermissionWrites.Should().Be(0);
        f.TaskWrites.Should().NotContain(task => task.State == HostTaskState.Succeeded);
        source = true;
        f.CanControl = f.CanInspect = true;
        await FluentActions.Awaiting(() => commands.ExecuteAsync(command, RequestOrigin.ActivatedVoice, () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>("attempted confirmations consume the review");
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("status")]
    [InlineData("unsupported")]
    [InlineData("storage")]
    [InlineData("cancelled")]
    public async Task Permission_review_discard_replacement_or_failure_never_commits_or_replays(string transition)
    {
        using var f = new PermissionFixture();
        var commands = new SessionPermissionCommandService(f.Service, f, new RetentionCommandTime());
        var id = f.Session.SessionId.Value;
        await commands.ExecuteAsync(new(SessionCommandOperation.PermissionReview, id) { PermissionLevel = SessionPermissionLevel.ModelReview },
            RequestOrigin.LocalUi, () => true, f.Token);
        var operation = transition switch
        {
            "cancel" => SessionCommandOperation.PermissionCancel,
            "status" => SessionCommandOperation.PermissionStatus,
            "unsupported" => SessionCommandOperation.Help,
            _ => SessionCommandOperation.PermissionConfirm,
        };
        if (transition is "storage") { f.PermissionWriteFailure = new IOException("Unconfirmed storage receipt"); }
        if (transition is "cancelled") { f.PermissionWriteFailure = new OperationCanceledException(f.Token); }
        var action = () => commands.ExecuteAsync(new(operation, id), RequestOrigin.LocalUi, () => true, f.Token);
        if (transition is "cancel" or "status")
        { (await action()).Outcome.Should().Be(transition is "cancel" ? "cancelled" : "observed"); }
        else { await FluentActions.Awaiting(action).Should().ThrowAsync<Exception>(); }
        f.Saved.Should().BeNull();
        f.PermissionWrites.Should().Be(0);
        f.TaskWrites.Should().NotContain(task => task.State == HostTaskState.Succeeded);
        await FluentActions.Awaiting(() => commands.ExecuteAsync(new(SessionCommandOperation.PermissionConfirm, id),
            RequestOrigin.LocalUi, () => true, f.Token)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("missing-id")]
    [InlineData("missing-level")]
    [InlineData("unknown-level")]
    [InlineData("done")]
    [InlineData("source")]
    [InlineData("late-source")]
    [InlineData("retire-in-admission")]
    [InlineData("system")]
    [InlineData("model")]
    [InlineData("cancelled")]
    public async Task Permission_review_never_stages_untrusted_or_changed_source(string failure)
    {
        using var f = new PermissionFixture();
        var commands = new SessionPermissionCommandService(f.Service, f, new RetentionCommandTime());
        var id = f.Session.SessionId.Value;
        var source = failure is not "source";
        if (failure is "done") { f.Session = f.Session with { IsActive = false }; }
        if (failure is "late-source") { f.BeforePermissionRead = () => { source = false; return Task.CompletedTask; }; }
        var admissions = 0;
        bool Admitted()
        {
            if (failure is "retire-in-admission" && ++admissions == 3) { commands.Retire(); }
            return source;
        }
        using var cancellation = new CancellationTokenSource();
        if (failure is "cancelled") { cancellation.Cancel(); }
        var origin = failure is "system" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi;
        using var ambient = failure is "model" ? HostActivity.BeginRoot(
            new(f.Request.RequestId, f.Request.SessionId, f.Request.TaskId, RequestOrigin.LocalUi, new(Guid.NewGuid())),
            HostActivityLayer.Application, HostOperation.Tool) : null;
        var command = new SessionCommand(SessionCommandOperation.PermissionReview, failure is "missing-id" ? null : id)
        {
            PermissionLevel = failure is "missing-level" ? null : failure is "unknown-level"
                ? (SessionPermissionLevel)99 : SessionPermissionLevel.ReviewAll,
        };
        await FluentActions.Awaiting(() => commands.ExecuteAsync(command, origin, Admitted, cancellation.Token))
            .Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().BeEmpty();
        f.Saved.Should().BeNull();
        await FluentActions.Awaiting(() => commands.ExecuteAsync(new(SessionCommandOperation.PermissionConfirm, id),
            origin, () => true, f.Token)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Permission_status_names_the_exact_conversation_without_recording_control_intent()
    {
        using var f = new PermissionFixture();
        f.ExactListReader = session => new(f.Session, new(session, new(1), new("Project alpha")));
        var commands = new SessionPermissionCommandService(f.Service, f, new RetentionCommandTime());
        var observed = await commands.ExecuteAsync(new(SessionCommandOperation.PermissionStatus, f.Session.SessionId.Value),
            RequestOrigin.LocalUi, () => true, f.Token);
        observed.Message.Should().Contain("Project alpha").And.Contain(f.Session.SessionId.Value.ToString("D"));
        f.TaskWrites.Should().BeEmpty();
        f.Saved.Should().BeNull();
    }

    [Fact]
    public async Task Permission_review_continues_across_fresh_capture_but_never_skips_current_capture_admission()
    {
        using var f = new PermissionFixture();
        var commands = new SessionPermissionCommandService(f.Service, f, new RetentionCommandTime());
        var id = f.Session.SessionId.Value;
        var capture = true;
        var source = true;
        await commands.ExecuteAsync(new(SessionCommandOperation.PermissionReview, id) { PermissionLevel = SessionPermissionLevel.ReviewAll },
            RequestOrigin.ActivatedVoice, () => capture, f.Token, () => source);
        capture = false;
        (await commands.ExecuteAsync(new(SessionCommandOperation.PermissionConfirm, id), RequestOrigin.ActivatedVoice,
            () => true, f.Token)).Outcome.Should().Be("committed");
        f.PermissionWrites.Should().Be(1);
        await FluentActions.Awaiting(() => commands.ExecuteAsync(new(SessionCommandOperation.PermissionReview, id)
            { PermissionLevel = SessionPermissionLevel.ApproveAll }, RequestOrigin.ActivatedVoice,
            () => capture, f.Token, () => source)).Should().ThrowAsync<InvalidOperationException>();
        capture = true;
        source = false;
        await FluentActions.Awaiting(() => commands.ExecuteAsync(new(SessionCommandOperation.PermissionReview, id)
            { PermissionLevel = SessionPermissionLevel.ApproveAll }, RequestOrigin.ActivatedVoice,
            () => capture, f.Token, () => source)).Should().ThrowAsync<InvalidOperationException>();
        f.PermissionWrites.Should().Be(1);
    }

    [Fact]
    public async Task Permission_read_completing_after_retirement_cannot_restore_a_review()
    {
        using var f = new PermissionFixture();
        var commands = new SessionPermissionCommandService(f.Service, f, new RetentionCommandTime());
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.BeforePermissionRead = async () => { reading.SetResult(); await release.Task; };
        var review = commands.ExecuteAsync(new(SessionCommandOperation.PermissionReview, f.Session.SessionId.Value)
        { PermissionLevel = SessionPermissionLevel.ApproveAll }, RequestOrigin.LocalUi, () => true, f.Token);
        await reading.Task;
        commands.Retire();
        release.SetResult();
        await FluentActions.Awaiting(() => review).Should().ThrowAsync<InvalidOperationException>();
        f.TaskWrites.Should().BeEmpty();
        await FluentActions.Awaiting(() => commands.ExecuteAsync(new(SessionCommandOperation.PermissionConfirm, f.Session.SessionId.Value),
            RequestOrigin.LocalUi, () => true, f.Token)).Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed partial class PermissionFixture
    {
        internal int PermissionWrites { get; private set; }
        internal Func<Task>? BeforePermissionRead { get; set; }
        internal Action? BeforePermissionWrite { get; set; }
        internal Exception? PermissionWriteFailure { get; set; }
    }
}
