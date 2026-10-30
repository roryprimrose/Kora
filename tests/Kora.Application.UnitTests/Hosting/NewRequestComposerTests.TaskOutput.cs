using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class NewRequestComposerTests
{
    [Theory]
    [InlineData(TaskResponseMode.Inherit, ResponseOutputMode.Hybrid)]
    [InlineData(TaskResponseMode.VisualOnly, ResponseOutputMode.VisualOnly)]
    [InlineData(TaskResponseMode.Hybrid, ResponseOutputMode.Hybrid)]
    [InlineData(TaskResponseMode.VoiceOnly, ResponseOutputMode.VoiceOnly)]
    public async Task TaskOutputReviewBindsOnlyNewRetainedRequestAndConsumesExactTerminalOnce(
        TaskResponseMode mode, ResponseOutputMode configured)
    {
        using var f = new Fixture();
        await f.Composer.Create();
        var commands = new SessionRequestCommandService(f.Workspace, f.Conversation, f.Access, TimeProvider.System);
        ConversationSubmissionReceipt? presented = null;
        TaskResponseOutputReview? output = null;
        commands.BindTaskResponseOutput((declared, _) => output = new(declared, ResponseOutputMode.Hybrid,
            () => true, (receipt, selected, current, _) =>
            {
                selected.Declared.Should().Be(mode);
                selected.Configured.Should().Be(configured);
                current().Should().BeTrue();
                presented = receipt;
                return Task.CompletedTask;
            }));
        var review = (await commands.ExecuteAsync(ReviewCommand(f.Entry!, " unchanged original ")
            with { RequestTaskResponseMode = mode }, RequestOrigin.LocalUi, () => true,
            TestContext.Current.CancellationToken)).RequestReview!;
        f.Retentions.Should().Be(0);
        presented.Should().BeNull();
        review.TaskResponseMode.Should().Be(mode);
        review.ConfiguredTaskResponseMode.Should().Be(configured);
        var result = await commands.ExecuteAsync(Confirm(review), RequestOrigin.LocalUi, () => true, TestContext.Current.CancellationToken);
        presented.Should().Be(result.RequestSubmission);
        presented!.Message.Body.Should().Be(" unchanged original ");
        var exactOutput = output ?? throw new InvalidOperationException("The host output review was not captured.");
        await exactOutput.Invoking(value => value.PresentAsync(presented, f.Entry!, () => true, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        await exactOutput.Invoking(value => Task.Run(() => value.Bind(presented.Message.Request), TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        await f.Conversation.SubmitAsync(f.Entry!, "later ordinary", () => true, TestContext.Current.CancellationToken);
        presented.Message.Body.Should().Be(" unchanged original ", "later ordinary submission cannot reuse output authority");
    }

    [Theory]
    [InlineData("missing-host")]
    [InlineData("provider")]
    [InlineData("local-profile")]
    [InlineData("cancel")]
    [InlineData("retire")]
    [InlineData("expiry")]
    public async Task OutputReviewRefusesUnsupportedOrRetiredScopeBeforeRetention(string change)
    {
        using var f = new Fixture();
        await f.Composer.Create();
        var time = new RequestTime();
        var service = new SessionRequestCommandService(f.Workspace, f.Conversation, f.Access, time);
        if (change is not "missing-host")
        { service.BindTaskResponseOutput((mode, _) => new(mode, ResponseOutputMode.Hybrid, () => true,
            (_, _, _, _) => throw new InvalidOperationException("No output is allowed."))); }
        var command = ReviewCommand(f.Entry!, "original") with { RequestTaskResponseMode = TaskResponseMode.VoiceOnly };
        if (change is "provider") { command = command with { RequestProvider = SessionRequestReview.ProviderChoice.Copilot }; }
        if (change is "local-profile") { command = command with { RequestLocalProfile = new(Kora.Core.Providers.ProviderService.Local,
            new("account"), new(1), new("model"), new("reasoning"), new("context")) }; }
        if (change is "missing-host" or "provider" or "local-profile")
        {
            await service.Invoking(value => value.ExecuteAsync(command, RequestOrigin.LocalUi, () => true,
                TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            var review = (await service.ExecuteAsync(command, RequestOrigin.LocalUi, () => true,
                TestContext.Current.CancellationToken)).RequestReview!;
            if (change is "cancel")
            { await service.ExecuteAsync(Confirm(review) with { Operation = SessionCommandOperation.RequestCancel },
                RequestOrigin.LocalUi, () => true, TestContext.Current.CancellationToken); }
            if (change is "retire") { service.Retire(); }
            if (change is "expiry") { time.Now = review.ExpiresAt; }
            await service.Invoking(value => value.ExecuteAsync(Confirm(review), RequestOrigin.LocalUi, () => true,
                TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        }
        f.Retentions.Should().Be(0);
        f.ModelAdmissions.Should().Be(0);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void TaskOutputRejectsUnknownModeWithoutDefaultReinterpretation(int declared, int inherited)
    {
        var create = () => new TaskResponseOutputReview((TaskResponseMode)declared, (ResponseOutputMode)inherited,
            () => true, (_, _, _, _) => Task.CompletedTask);
        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("host")]
    [InlineData("source")]
    [InlineData("cancelled")]
    [InlineData("wrong-request")]
    [InlineData("wrong-task")]
    [InlineData("wrong-session")]
    [InlineData("wrong-generation")]
    [InlineData("not-terminal")]
    public async Task TaskOutputCannotPresentWithoutExactTerminalIdentityAndOriginalLiveAuthority(string fault)
    {
        using var f = new Fixture();
        await f.Composer.Create();
        var receipt = await f.Conversation.SubmitAsync(f.Entry!, "original", () => true, TestContext.Current.CancellationToken);
        var host = true;
        var calls = 0;
        var output = new TaskResponseOutputReview(TaskResponseMode.VoiceOnly, ResponseOutputMode.Hybrid,
            () => host, (_, _, _, _) => { calls++; return Task.CompletedTask; });
        output.Bind(receipt.Message.Request);
        var presented = receipt;
        var target = f.Entry!;
        if (fault is "host") { host = false; }
        if (fault is "wrong-request") { presented = receipt with { Message = receipt.Message with { Request = HostRequest.Create(RequestOrigin.LocalUi) } }; }
        if (fault is "wrong-task") { presented = receipt with { Task = new(HostRequest.Create(RequestOrigin.LocalUi), new(1), HostTaskState.Unavailable) }; }
        if (fault is "wrong-session") { target = target with { Authority = target.Authority with { SessionId = new(Guid.NewGuid()) } }; }
        if (fault is "wrong-generation") { target = target with { Authority = target.Authority with { Generation = new(2) } }; }
        if (fault is "not-terminal") { presented = receipt with { Message = receipt.Message with { Outcome = null } }; }
        using var cancelled = new CancellationTokenSource();
        if (fault is "cancelled") { cancelled.Cancel(); }
        var operation = () => output.PresentAsync(presented, target, () => fault is not "source", cancelled.Token);
        if (fault.StartsWith("wrong-", StringComparison.Ordinal) || fault is "not-terminal")
        { await operation.Should().ThrowAsync<InvalidOperationException>(); }
        else if (fault is "cancelled") { await operation.Should().ThrowAsync<OperationCanceledException>(); }
        else
        {
            await operation();
            output.Status.Should().Contain("withheld");
        }
        calls.Should().Be(0);
    }

    [Fact]
    public void RevokedOutputCannotBindEvenBeforeARequestExists()
    {
        var output = new TaskResponseOutputReview(TaskResponseMode.Hybrid, ResponseOutputMode.Hybrid, () => false,
            (_, _, _, _) => Task.CompletedTask);
        var bind = () => output.Bind(HostRequest.Create(RequestOrigin.LocalUi));
        bind.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("throw")]
    [InlineData("changed-source")]
    public async Task ConfirmedRetentionDoesNotGrantLateOrChangedOutputAuthority(string fault)
    {
        using var f = new Fixture();
        await f.Composer.Create();
        var calls = 0;
        var output = new TaskResponseOutputReview(TaskResponseMode.VoiceOnly, ResponseOutputMode.Hybrid,
            () => true, (_, _, _, _) =>
            {
                calls++;
                if (fault is "throw") { throw new IOException("synthetic presentation unavailable"); }
                return Task.CompletedTask;
            });
        var terminalReads = 0;
        if (fault is "changed-source")
        {
            f.AfterComplete = () => f.AfterRead = () =>
            {
                if (++terminalReads == 3)
                { f.Entry = f.Entry! with { Authority = f.Entry!.Authority with { Generation = new(2) } }; }
            };
        }
        if (fault is "expired")
        {
            f.AfterComplete = () => f.Access.CanControl = false;
        }
        var operation = () => f.Conversation.SubmitAsync(f.Entry!, "original", () => true,
            TestContext.Current.CancellationToken, taskOutput: output);
        if (fault is "throw") { await operation.Should().ThrowAsync<IOException>(); calls.Should().Be(1); }
        else if (fault is "changed-source")
        {
            (await operation()).Message.Should().Be(f.Retained);
            output.Status.Should().Contain("withheld");
            calls.Should().Be(0);
        }
        else
        {
            await operation.Should().ThrowAsync<Exception>();
            calls.Should().Be(0);
        }
    }

    [Fact]
    public async Task OneTaskOutputCannotBeAttachedToAMandatoryQuestionSubmission()
    {
        using var f = new Fixture();
        await f.Composer.Create();
        var output = new TaskResponseOutputReview(TaskResponseMode.VoiceOnly, ResponseOutputMode.Hybrid, () => true,
            (_, _, _, _) => Task.CompletedTask);
        var operation = () => f.Conversation.SubmitAsync(f.Entry!, "original", () => true,
            TestContext.Current.CancellationToken, questionPresenter: (_, _, _, _, _, _) =>
                throw new InvalidOperationException("The question must never be reached."), taskOutput: output);
        await operation.Should().ThrowAsync<InvalidOperationException>();
        f.Retentions.Should().Be(0);
    }
}
