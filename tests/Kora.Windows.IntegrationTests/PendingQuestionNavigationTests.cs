using System.Reflection;
using Avalonia.Controls;
using AwesomeAssertions;
using Kora.Application;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class PendingQuestionNavigationTests
{
    [Fact]
    public async Task ExactNavigationFocusesSameControllerWithoutReloadOrDraftReplacementAndAnswerRemainsSeparate()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            using var host = Host(f);
            using var controller = Controller(f, host);
            var shown = new TaskCompletionSource<QuestionWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<QuestionWindow>((window, _) => shown.TrySetResult(window));
            var owner = new Window();
            owner.Show();
            var run = controller.ReviewVersionAsync(owner);
            QuestionWindow? window = null;
            NativeQuestionViewModel? model = null;
            try
            {
                window = await shown.Task.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
                model = State(controller) ?? throw new InvalidOperationException("The shown question has no state.");
                await EditableAsync(model, f.Token);
                model.Edit(new(["show"]));
                await model.SaveDraftAsync();
                model.Edit(new([]));
                var question = model.Question;
                var answer = model.Answer;
                var status = model.Status;
                var body = f.Main.ResponseBody;
                var transcript = f.Main.Transcript;
                f.Main.CommandText = "independent unfinished composer";
                owner.Activate();
                var target = controller.CaptureFocus(question.Key.Request.SessionId.Value, question.Key.Request.TaskId.Value);
                target.Should().NotBeNull();
                (await host.ValidateFocusAsync(model)).Should().BeTrue();
                host.CaptureFocusEligibility(model)!().Should().BeTrue();
                var focused = await target!.FocusAsync(static () => true);
                focused.Should().BeTrue($"active={window.IsActive}, editable={model.IsEditable}, busy={model.HasPendingOperation}, same={ReferenceEquals(model.Question, question)}, status={model.Status}");
                (await target.FocusAsync(static () => true)).Should().BeFalse();
                window.IsActive.Should().BeTrue();
                State(controller).Should().BeSameAs(model);
                model.Question.Should().BeSameAs(question);
                model.Answer.Should().BeSameAs(answer);
                model.Status.Should().Be(status);
                model.Completion.IsCompleted.Should().BeFalse();
                run.IsCompleted.Should().BeFalse();
                (await f.Interactions.ReadTaskAsync(question.Key.Request.SessionId, question.Key.Request.TaskId, f.Token))!
                    .Task.State.Should().Be(HostTaskState.IntentRecorded);
                f.Main.ResponseBody.Should().Be(body);
                f.Main.Transcript.Should().Be(transcript);
                f.Main.CommandText.Should().Be("independent unfinished composer");
                model.Edit(new(["show"]));
                await model.SubmitAsync();
                await run.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
                (await model.Completion).Question!.Status.Should().Be(QuestionStatus.Answered);
                controller.CaptureFocus(question.Key.Request.SessionId.Value, question.Key.Request.TaskId.Value).Should().BeNull();
            }
            finally
            {
                await CloseAndDrainAsync(controller, owner, window, model, run);
            }
        });
    }

    [Theory]
    [InlineData("session")]
    [InlineData("task")]
    [InlineData("admission")]
    [InlineData("privacy")]
    [InlineData("close")]
    [InlineData("cancel")]
    [InlineData("answer")]
    [InlineData("dispose")]
    [InlineData("hide-reshow")]
    [InlineData("activation-revoked")]
    public async Task CapturedNavigationCannotFollowStaleOrForeignTargets(string loss)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            using var host = Host(f);
            using var controller = Controller(f, host);
            var shown = new TaskCompletionSource<QuestionWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<QuestionWindow>((window, _) => shown.TrySetResult(window));
            var owner = new Window();
            owner.Show();
            var run = controller.ReviewVersionAsync(owner);
            QuestionWindow? window = null;
            NativeQuestionViewModel? model = null;
            try
            {
                window = await shown.Task.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
                model = State(controller) ?? throw new InvalidOperationException("The shown question has no state.");
                await EditableAsync(model, f.Token);
                var key = model.Key;
                var expiry = model.Question.ExpiresAt;
                var target = controller.CaptureFocus(key.Request.SessionId.Value, key.Request.TaskId.Value)!;
                switch (loss)
                {
                    case "session":
                        controller.CaptureFocus(Guid.NewGuid(), key.Request.TaskId.Value).Should().BeNull();
                        break;
                    case "task":
                        controller.CaptureFocus(key.Request.SessionId.Value, Guid.NewGuid()).Should().BeNull();
                        break;
                    case "privacy": f.Access.SetOpen(false); break;
                    case "close": window.Close(); break;
                    case "cancel": await model.CancelAsync(); break;
                    case "answer": model.Edit(new(["show"])); await model.SubmitAsync(); break;
                    case "dispose": controller.Dispose(); break;
                    case "hide-reshow": window.Hide(); window.Show(owner); break;
                    case "activation-revoked":
                        owner.Activate();
                        window.Activated += (_, _) => controller.Dispose();
                        break;
                }
                if (loss is not ("session" or "task"))
                {
                    (await target.FocusAsync(() => !string.Equals(loss, "admission", StringComparison.Ordinal))).Should().BeFalse();
                }
                model.Question.ExpiresAt.Should().Be(expiry);
            }
            finally
            {
                await CloseAndDrainAsync(controller, owner, window, model, run);
            }
        });
    }

    [Fact]
    public async Task EarlyPresentationFailureDrainsOwnedStorageOperationBeforeFixtureCleanup()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var checkpoint = new InteractionTransactionCheckpoint();
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var f = new NativeUxFixtureSession(Path.GetTempPath(), interactionCheckpoint: checkpoint);
            await f.InitializeAsync();
            using var host = Host(f);
            using var controller = Controller(f, host);
            var owner = new Window();
            owner.Show();
            checkpoint.Commit = (_, _) =>
            {
                checkpoint.Commit = null;
                entered.TrySetResult();
                release.Wait(f.Token);
            };
            var run = controller.ReviewVersionAsync(owner);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
                var failed = FailBeforePresentationAsync();
                try
                {
                    failed.IsCompleted.Should().BeFalse();
                    run.IsCompleted.Should().BeFalse();
                    Directory.Exists(f.LocalRoot).Should().BeTrue();
                }
                finally { release.Set(); }
                Func<Task> observeFailure = () => failed;
                await observeFailure.Should().ThrowAsync<InvalidOperationException>()
                    .WithMessage("Synthetic presentation setup failure.");
                run.IsCompleted.Should().BeTrue();
                f.Dispose();
                Directory.Exists(f.LocalRoot).Should().BeFalse();
            }
            finally
            {
                release.Set();
                await CloseAndDrainAsync(controller, owner, null, null, run);
            }

            async Task FailBeforePresentationAsync()
            {
                try { throw new InvalidOperationException("Synthetic presentation setup failure."); }
                finally { await CloseAndDrainAsync(controller, owner, null, null, run); }
            }
        });
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("generation")]
    [InlineData("source")]
    [InlineData("revision")]
    public async Task MetadataValidationRejectsChangedAuthorityWithoutReadingOrRestoringBody(string loss)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await f.RunAsync(() => f.Store.PublishTrustedSnapshotAsync(f.Request, f.Policy, null, f.ObservationRevision, f.Token));
        var presented = await f.RunAsync(() => f.Questions.CreateAsync(f.Request, LocalVersionWait.CreateSpec(),
            f.Time.Now.AddMinutes(5), f.Token));
        var question = presented.Question!;
        await f.RunAsync(async () => await f.Store.AdmitVersionWaitAsync(question.Key, f.Token));
        (await f.Store.ValidateQuestionFocusAsync(question, f.Token)).Should().BeTrue();
        switch (loss)
        {
            case "expiry": f.Time.Now += TimeSpan.FromMinutes(6); break;
            case "generation":
                (await f.Store.ValidateQuestionFocusAsync(question with { SessionGeneration = new(2) }, f.Token)).Should().BeFalse();
                return;
            case "source":
                f.Reopen();
                await f.Store.InitializeAsync(f.Token);
                break;
            case "revision":
                await f.RunAsync(() => f.Questions.DraftAsync(question.Key, new(["show"]), RequestOrigin.LocalUi, f.Token));
                break;
        }
        (await f.Store.ValidateQuestionFocusAsync(question, f.Token)).Should().BeFalse();
    }

    private static NativeQuestionHost Host(NativeUxFixtureSession f)
    {
        var host = new NativeQuestionHost(f.Interactions, TimeProvider.System, NullLogger<NativeQuestionViewModel>.Instance);
        host.BindWorkspace(f.Sessions);
        host.BindPrivateRevision(() => f.Access.ControlRevision);
        return host;
    }

    private static QuestionWindowController Controller(NativeUxFixtureSession f, NativeQuestionHost host) =>
        new(f.Main, host, f.VersionQuery, new AssemblyApplicationInfo(() => "synthetic-focus-version"),
            new(NullLogger<NativeDetailRenderer>.Instance), () => f.Access.Open,
            NullLogger<QuestionWindowController>.Instance, () => f.Access.ControlRevision);

    private static NativeQuestionViewModel? State(QuestionWindowController controller) =>
        (NativeQuestionViewModel?)typeof(QuestionWindowController).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(controller);

    private static async Task CloseAndDrainAsync(QuestionWindowController controller, Window owner,
        QuestionWindow? window, NativeQuestionViewModel? model, Task run)
    {
        var pending = model ?? State(controller);
        controller.Dispose();
        window?.Close();
        owner.Close();
        try { await run; }
        finally
        {
            if (pending is not null) { await QuiescentAsync(pending); }
        }
    }

    private static async Task EditableAsync(NativeQuestionViewModel model, CancellationToken token)
    {
        if (model.IsEditable) { return; }
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (model.IsEditable) { ready.TrySetResult(); }
        }
        model.PropertyChanged += Changed;
        try
        {
            if (model.IsEditable) { ready.TrySetResult(); }
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        }
        finally { model.PropertyChanged -= Changed; }
    }

    private static async Task QuiescentAsync(NativeQuestionViewModel model)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (!model.HasPendingOperation) { ready.TrySetResult(); }
        }
        model.PropertyChanged += Changed;
        try
        {
            if (!model.HasPendingOperation) { ready.TrySetResult(); }
            await ready.Task;
        }
        finally { model.PropertyChanged -= Changed; }
    }
}
