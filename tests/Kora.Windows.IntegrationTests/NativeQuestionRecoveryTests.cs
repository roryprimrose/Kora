using System.Diagnostics;
using System.ComponentModel;
using System.Collections.Concurrent;

using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Application.Auditing;
using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Session;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class NativeQuestionRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_close_clears_controls_then_exact_reopen_restores_only_the_saved_draft_without_changing_other_focus_targets(bool externalCancel)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            using var host = CreateHost(f, new RecoveryClock(f.Time));
            var taskControls = WindowsSqliteSessionWorkspaceTests.Service(f, new());
            host.BindWorkspace(taskControls);
            using var logs = LoggerFactory.Create(_ => { });
            var shown = new TaskCompletionSource<NativeQuestionViewModel>(TaskCreationOptions.RunContinuationsAsynchronously);
            var run = CreateQuery(f, logs).RunAsync(RequestOrigin.LocalUi, () => Task.CompletedTask, f.Token,
                _ => host.AskVersionAsync(model =>
                {
                    shown.SetResult(model);
                    return Task.CompletedTask;
                }, f.Token));
            var model = await shown.Task;
            var renderer = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance);
            var first = new QuestionWindow(model, renderer);
            QuestionWindow? second = null;
            try
            {
                await ShowQuestionAsync(first, model);
                model.Edit(new(["show"]));
                await model.SaveDraftAsync();
                await model.ReviewAsync();
                model.Edit(new([]));
                first.Close();
                first.FindControl<TextBox>("AnswerInput")!.Text.Should().BeNullOrEmpty();
                first.FindControl<TextBox>("ExactReview")!.Text.Should().BeNullOrEmpty();
                first.FindControl<TextBlock>("PromptLabel")!.Text.Should().BeNullOrEmpty();
                first.FindControl<TextBlock>("TargetLabel")!.Text.Should().BeNullOrEmpty();
                first.FindControl<TextBlock>("QuestionStatus")!.Text.Should().BeNullOrEmpty();
                first.FindControl<StackPanel>("ChoicePanel")!.Children.Should().BeEmpty();
                run.IsCompleted.Should().BeFalse();
                model.Completion.IsCompleted.Should().BeFalse();
                var restored = await host.ReturnToCurrentQuestionAsync(f.Token);
                restored.Should().BeSameAs(model);
                second = new(restored!, renderer);
                await ShowQuestionAsync(second, model);
                model.Key.Revision.Value.Should().Be(2);
                model.Answer.Choices.Should().Equal("show");
                second.FocusManager!.GetFocusedElement().Should().NotBeSameAs(second.FindControl<Button>("SubmitAnswer"));
                second.FindControl<TextBox>("ExactReview")!.Text.Should().BeNullOrEmpty();
                if (externalCancel)
                {
                    var target = new HostTaskCancellationTarget(model.Key.Request.SessionId, model.Key.Request.TaskId,
                        new(1), model.Question.SessionGeneration, model.Key.QuestionId, model.Key.Revision);
                    // A typed/voice cancellation can originate outside the native UI thread.
                    await Task.Run(() => taskControls.CancelTaskAsync(target, RequestOrigin.LocalUi, () => true, f.Token), f.Token);
                }
                else { await model.CancelAsync(); }
                (await run.WaitAsync(TimeSpan.FromSeconds(5), f.Token)).State.Should().Be(HostTaskState.Cancelled);
            }
            finally
            {
                first.Close();
                second?.Close();
                host.Dispose();
            }
        });
    }

    [WindowsFact]
    public async Task Dismissed_unsaved_affirmative_edit_is_not_preselected_on_return()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        using var host = CreateHost(f, new RecoveryClock(f.Time));
        using var logs = LoggerFactory.Create(_ => { });
        var shown = new TaskCompletionSource<NativeQuestionViewModel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = CreateQuery(f, logs).RunAsync(RequestOrigin.LocalUi, () => Task.CompletedTask, f.Token,
            _ => host.AskVersionAsync(model =>
            {
                shown.SetResult(model);
                return Task.CompletedTask;
            }, f.Token));
        var model = await shown.Task;
        model.Edit(new(["show"]));
        model.Dismiss();
        (await host.ReturnToCurrentQuestionAsync(f.Token)).Should().BeSameAs(model);
        model.Answer.Choices.Should().BeEmpty();
        model.CanSubmit.Should().BeFalse();
        model.Key.Revision.Value.Should().Be(1);
        await model.CancelAsync();
        (await run).State.Should().Be(HostTaskState.Cancelled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dismissal_during_a_committed_draft_or_submit_cannot_publish_late_edits_or_dispatch_twice(bool submit)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        using var host = CreateHost(f, new RecoveryClock(f.Time));
        using var logs = LoggerFactory.Create(_ => { });
        var shown = new TaskCompletionSource<NativeQuestionViewModel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocations = 0;
        var run = CreateQuery(f, logs).RunAsync(RequestOrigin.LocalUi, () =>
        {
            invocations++;
            return Task.CompletedTask;
        }, f.Token, _ => host.AskVersionAsync(model =>
        {
            shown.SetResult(model);
            return Task.CompletedTask;
        }, f.Token));
        var model = await shown.Task;
        model.Edit(new(["show"]));
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        checkpoint.Commit = (_, _) =>
        {
            writing.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5), f.Token)) { throw new TimeoutException("Owned commit gate was not released."); }
        };
        var operation = submit ? model.SubmitAsync() : model.SaveDraftAsync();
        try
        {
            await writing.Task.WaitAsync(TimeSpan.FromSeconds(5), f.Token);
            model.Dismiss();
            (await host.ReturnToCurrentQuestionAsync(f.Token)).Should().BeNull();
        }
        finally
        {
            checkpoint.Commit = null;
            release.Set();
        }
        await operation;
        model.HasPresentation.Should().BeFalse();
        model.Answer.Choices.Should().BeEmpty();
        model.ReviewText.Should().BeEmpty();
        if (submit)
        {
            (await run).State.Should().Be(HostTaskState.Succeeded);
            (await host.ReturnToCurrentQuestionAsync(f.Token)).Should().BeNull();
            invocations.Should().Be(1);
        }
        else
        {
            model.Completion.IsCompleted.Should().BeFalse();
            (await host.ReturnToCurrentQuestionAsync(f.Token)).Should().BeSameAs(model);
            model.Answer.Choices.Should().Equal("show");
            model.Key.Revision.Value.Should().Be(2);
            await model.CancelAsync();
            (await run).State.Should().Be(HostTaskState.Cancelled);
            invocations.Should().Be(0);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dismiss_and_return_reuse_the_original_live_wait_committed_draft_and_exactly_one_terminal_receipt(bool cancel)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var spans = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Kora.Desktop",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);
        var clock = new RecoveryClock(f.Time);
        using var host = CreateHost(f, clock);
        using var logs = LoggerFactory.Create(_ => { });
        var query = CreateQuery(f, logs);
        var shown = new TaskCompletionSource<NativeQuestionViewModel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatches = 0;
        ActivityContext originalContext = default;
        var run = query.RunAsync(RequestOrigin.LocalUi, () =>
        {
            dispatches++;
            return Task.CompletedTask;
        }, f.Token, _ => host.AskVersionAsync(model =>
        {
            originalContext = Activity.Current!.Context;
            shown.SetResult(model);
            return Task.CompletedTask;
        }, f.Token));
        var model = await shown.Task;
        var original = model.Key;
        var expiry = model.Question.ExpiresAt;
        using (var admitted = HostActivity.BeginRoot(original.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            var duplicate = () => host.AskVersionAsync(_ => throw new InvalidOperationException("Must not present again"), f.Token);
            await duplicate.Should().ThrowAsync<InvalidOperationException>();
        }
        model.CanSubmit.Should().BeFalse();
        model.Edit(new(["show"]));
        await model.SaveDraftAsync();
        var saved = model.Key;
        saved.Revision.Value.Should().Be(2);
        await model.ReviewAsync();
        model.ReviewText.Should().NotBeEmpty();
        model.Edit(new([]));
        var tasks = f.Count("host_tasks");
        var questions = f.Count("host_questions");
        var activity = await f.Store.ReadRetentionAsync(original.Request.SessionId, f.Token);
        model.Dismiss();
        model.Completion.IsCompleted.Should().BeFalse();
        run.IsCompleted.Should().BeFalse();
        model.HasPresentation.Should().BeFalse();
        model.Answer.Choices.Should().BeEmpty();
        model.ReviewText.Should().BeEmpty();
        clock.Advance(TimeSpan.FromMinutes(2));

        NativeQuestionViewModel? restored;
        using (var hostile = new Activity("untrusted.copied-question").Start())
        {
            hostile.SetBaggage("kora.session.id", Guid.NewGuid().ToString("D"));
            restored = await host.ReturnToCurrentQuestionAsync(f.Token);
        }
        restored.Should().BeSameAs(model);
        model.Key.Should().Be(saved);
        model.Question.ExpiresAt.Should().Be(expiry);
        model.Answer.Choices.Should().Equal("show");
        model.ReviewText.Should().BeEmpty();
        model.CanSubmit.Should().BeTrue();
        f.Count("host_tasks").Should().Be(tasks);
        f.Count("host_questions").Should().Be(questions);
        (await f.Store.ReadRetentionAsync(original.Request.SessionId, f.Token)).Should().BeEquivalentTo(activity);
        using (var root = HostActivity.BeginRoot(original.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            (await f.Questions.SubmitAsync(original, new(["show"]), RequestOrigin.LocalUi, f.Token)).Outcome
                .Should().Be(HostInteractionOutcome.Conflict);
        }
        if (cancel) { await model.CancelAsync(); }
        else { await Task.WhenAll(model.SubmitAsync(), model.SubmitAsync()); }
        var receipt = await run;
        receipt.Request.Should().Be(original.Request);
        receipt.State.Should().Be(cancel ? HostTaskState.Cancelled : HostTaskState.Succeeded);
        dispatches.Should().Be(cancel ? 0 : 1);
        (await f.Tasks.ReadTaskAsync(original.Request.TaskId, f.Token)).Should().Be(receipt);
        (await f.Store.ReadQuestionsAsync(original.Request.SessionId, f.Token)).Should().ContainSingle()
            .Which.Key.Revision.Value.Should().Be(3);
        (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
        (await host.ReturnToCurrentQuestionAsync(f.Token)).Should().BeNull();
        clock.Timer.IsDisposed.Should().BeTrue();
        clock.Timers.Should().OnlyContain(timer => timer.IsDisposed);
        clock.Timer.Tick();
        (await host.ReturnToCurrentQuestionAsync(f.Token)).Should().BeNull();
        await model.SubmitAsync();
        dispatches.Should().Be(cancel ? 0 : 1);
        var presentations = spans.Where(span => Equals(span.GetTagItem("kora.request.id"), original.Request.RequestId.Value)).ToArray();
        presentations.Should().NotBeEmpty();
        presentations.Should().OnlyContain(span => span.ParentId == null && span.Status != ActivityStatusCode.Unset
            && !span.Baggage.Any() && span.Links.Any(link => link.Context == originalContext)
            && span.TraceId != originalContext.TraceId && span.Source.Version != null);
    }

    [WindowsFact]
    public async Task Reopening_an_already_presented_exact_question_preserves_unsaved_input_without_minting_another_target()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        using var host = CreateHost(f, new RecoveryClock(f.Time));
        var shown = new TaskCompletionSource<NativeQuestionViewModel>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        await new HostTaskCoordinator(f.Tasks).RecordIntentAsync(root.Request, f.Token);
        var run = host.AskVersionAsync(model =>
        {
            shown.SetResult(model);
            return Task.CompletedTask;
        }, f.Token);
        var model = await shown.Task;
        var original = model.Key;
        model.Edit(new(["show"]));
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        (await host.ReturnToCurrentQuestionAsync(f.Token)).Should().BeSameAs(model);
        model.Key.Should().Be(original);
        model.Answer.Choices.Should().Equal("show");
        model.Key.Revision.Value.Should().Be(1);
        await model.CancelAsync();
        (await run).State.Should().Be(HostTaskState.Cancelled);
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("monotonic")]
    [InlineData("privacy")]
    [InlineData("ownership-aba")]
    [InlineData("dispose")]
    [InlineData("unknown")]
    [InlineData("terminal")]
    [InlineData("revision")]
    [InlineData("generation")]
    [InlineData("storage")]
    public async Task Hidden_wait_is_finite_and_withdrawn_when_original_lifetime_or_authority_ends(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var clock = new RecoveryClock(f.Time);
        var allowed = true;
        using var host = CreateHost(f, clock, () => allowed);
        var admission = 1L;
        host.BindAdmissionRevision(() => admission);
        using var logs = LoggerFactory.Create(_ => { });
        var shown = new TaskCompletionSource<NativeQuestionViewModel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invoked = false;
        var run = CreateQuery(f, logs).RunAsync(RequestOrigin.LocalUi, () =>
        {
            invoked = true;
            return Task.CompletedTask;
        }, f.Token, _ => host.AskVersionAsync(model =>
        {
            shown.SetResult(model);
            return Task.CompletedTask;
        }, f.Token));
        var model = await shown.Task;
        var key = model.Key;
        model.Dismiss();
        if (change is "expiry") { clock.Advance(TimeSpan.FromMinutes(5)); }
        if (change is "monotonic")
        {
            f.Time.MonotonicTicks += TimeSpan.FromMinutes(5).Ticks;
            f.Time.Now = f.Time.Now.AddDays(-1);
        }
        if (change is "privacy") { allowed = false; }
        if (change is "ownership-aba") { admission++; }
        if (change is "dispose") { host.Dispose(); }
        if (change is "unknown" or "terminal" or "revision" or "generation")
        {
            using var original = HostActivity.BeginRoot(key.Request, HostActivityLayer.Application, HostOperation.Request);
            if (change is "unknown" or "terminal")
            {
                var intent = (await f.Tasks.ReadTaskAsync(key.Request.TaskId, f.Token))!;
                await new HostTaskCoordinator(f.Tasks).RecordOutcomeAsync(intent,
                    change is "unknown" ? HostTaskState.Unknown : HostTaskState.Failed, f.Token);
            }
            if (change is "revision")
            {
                await f.Questions.ReviseAsync(key, model.Question.Spec, model.Question.ExpiresAt, f.Token);
            }
            if (change is "generation")
            {
                await f.Store.SetSessionLifecycleAsync(key.Request, new(1), active: false, remove: false, f.Token);
            }
        }
        if (change is "storage")
        {
            Directory.Move(Path.Combine(f.Paths.LocalRoot, HostInteractionSchema.Partition),
                Path.Combine(f.Paths.LocalRoot, "OwnedUnavailableQuestionAuthority"));
        }
        clock.Timer.Tick();
        var finish = () => run;
        await finish.Should().ThrowAsync<Exception>();
        model.Completion.IsCompleted.Should().BeTrue();
        model.HasPresentation.Should().BeFalse();
        model.Answer.Choices.Should().BeEmpty();
        (await host.ReturnToCurrentQuestionAsync(f.Token)).Should().BeNull();
        allowed = true;
        clock.Timer.Tick();
        (await host.ReturnToCurrentQuestionAsync(f.Token)).Should().BeNull();
        invoked.Should().BeFalse();
        clock.Timer.IsDisposed.Should().BeTrue();
    }

    [WindowsFact]
    public async Task A_foreign_persisted_wait_approval_or_previous_host_cannot_mint_a_live_registration()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var wait = await WindowsSqliteTaskControlTests.WaitAsync(f);
        using var host = CreateHost(f, new RecoveryClock(f.Time));
        host.CreateState(wait).Key.Should().Be(wait.Key);
        (await host.ReturnToCurrentQuestionAsync(f.Token)).Should().BeNull();
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        var approval = await f.PresentAsync();
        host.CreateState(approval).IsApproval.Should().BeTrue();
        (await host.ReturnToCurrentQuestionAsync(f.Token)).Should().BeNull();
        f.Reopen();
        using var restarted = CreateHost(f, new RecoveryClock(f.Time));
        (await restarted.ReturnToCurrentQuestionAsync(f.Token)).Should().BeNull();
        (await f.Store.ReadTaskAsync(wait.Key.Request.SessionId, wait.Key.Request.TaskId, f.Token))!
            .CurrentSource.Should().BeFalse();
        (await new HostTaskCoordinator(f.Tasks).RecoverAsync(10, f.Token)).Should()
            .Contain(record => record.Request == wait.Key.Request && record.State == HostTaskState.Interrupted);
        (await restarted.ReturnToCurrentQuestionAsync(f.Token)).Should().BeNull();
        (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
    }

    [WindowsFact]
    public async Task Unknown_owner_admission_revision_never_enables_recovery_of_an_existing_native_wait()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        using var host = new NativeQuestionHost(f.Store, new RecoveryClock(f.Time), NullLogger<NativeQuestionViewModel>.Instance);
        var controls = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        host.BindWorkspace(controls);
        host.BindGate(static () => true);
        using var logs = LoggerFactory.Create(_ => { });
        var shown = new TaskCompletionSource<NativeQuestionViewModel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = CreateQuery(f, logs).RunAsync(RequestOrigin.LocalUi, () => Task.CompletedTask, f.Token,
            _ => host.AskVersionAsync(model =>
            {
                shown.SetResult(model);
                return Task.CompletedTask;
            }, f.Token));
        var model = await shown.Task;
        model.Dismiss();
        (await host.ReturnToCurrentQuestionAsync(f.Token)).Should().BeNull();
        var target = new HostTaskCancellationTarget(model.Key.Request.SessionId, model.Key.Request.TaskId,
            new(1), model.Question.SessionGeneration, model.Key.QuestionId, model.Key.Revision);
        await controls.CancelTaskAsync(target, RequestOrigin.LocalUi, () => true, f.Token);
        (await run).State.Should().Be(HostTaskState.Cancelled);
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task Non_native_original_lineage_cannot_register_a_recoverable_question(RequestOrigin origin)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        using var host = CreateHost(f, new RecoveryClock(f.Time));
        using var root = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        var questions = f.Count("host_questions");
        var ask = () => host.AskVersionAsync(_ => Task.CompletedTask, f.Token);
        await ask.Should().ThrowAsync<InvalidOperationException>();
        (await host.ReturnToCurrentQuestionAsync(f.Token)).Should().BeNull();
        f.Count("host_questions").Should().Be(questions);
    }

    [WindowsFact]
    public async Task Failed_required_reopen_audit_faults_the_original_wait_and_prevents_late_retry()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        var clock = new RecoveryClock(f.Time);
        using var host = CreateHost(f, clock);
        using var logs = LoggerFactory.Create(_ => { });
        var shown = new TaskCompletionSource<NativeQuestionViewModel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = CreateQuery(f, logs).RunAsync(RequestOrigin.LocalUi, () => throw new InvalidOperationException("Must not dispatch"),
            f.Token, _ => host.AskVersionAsync(model =>
            {
                shown.SetResult(model);
                return Task.CompletedTask;
            }, f.Token));
        var model = await shown.Task;
        model.Dismiss();
        checkpoint.Audit = (_, _) => throw new IOException("Required audit unavailable");
        (await host.ReturnToCurrentQuestionAsync(f.Token)).Should().BeNull();
        var finish = () => run;
        await finish.Should().ThrowAsync<IOException>();
        model.HasPresentation.Should().BeFalse();
        checkpoint.Audit = null;
        (await host.ReturnToCurrentQuestionAsync(f.Token)).Should().BeNull();
        (await f.Store.ReadQuestionsAsync(model.Key.Request.SessionId, f.Token)).Single().Status.Should().Be(QuestionStatus.Pending);
        clock.Timer.IsDisposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_reopen_review_cannot_restore_controls_after_privacy_or_disposal(bool dispose)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        var allowed = true;
        using var host = CreateHost(f, new RecoveryClock(f.Time), () => allowed);
        using var logs = LoggerFactory.Create(_ => { });
        var shown = new TaskCompletionSource<NativeQuestionViewModel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = CreateQuery(f, logs).RunAsync(RequestOrigin.LocalUi, () => throw new InvalidOperationException("Must not dispatch"),
            f.Token, _ => host.AskVersionAsync(model =>
            {
                shown.SetResult(model);
                return Task.CompletedTask;
            }, f.Token));
        var model = await shown.Task;
        model.Edit(new(["show"]));
        await model.SaveDraftAsync();
        model.Dismiss();
        var reviewing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        checkpoint.Commit = (_, _) =>
        {
            reviewing.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5), f.Token)) { throw new TimeoutException("Owned review gate was not released."); }
        };
        var reopen = host.ReturnToCurrentQuestionAsync(f.Token);
        try
        {
            await reviewing.Task.WaitAsync(TimeSpan.FromSeconds(5), f.Token);
            if (dispose) { host.Dispose(); }
            else { allowed = false; }
        }
        finally
        {
            checkpoint.Commit = null;
            release.Set();
        }
        (await reopen).Should().BeNull();
        var finish = () => run;
        await finish.Should().ThrowAsync<Exception>();
        model.HasPresentation.Should().BeFalse();
        model.Answer.Choices.Should().BeEmpty();
        model.ReviewText.Should().BeEmpty();
        allowed = true;
        (await host.ReturnToCurrentQuestionAsync(f.Token)).Should().BeNull();
    }

    [WindowsFact]
    public async Task Presentation_callback_failure_and_cancellation_withdraw_the_registration_and_release_the_timer()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var clock = new RecoveryClock(f.Time);
        using var host = CreateHost(f, clock);
        using var logs = LoggerFactory.Create(_ => { });
        NativeQuestionViewModel? displayed = null;
        var failure = () => CreateQuery(f, logs).RunAsync(RequestOrigin.LocalUi, () => Task.CompletedTask,
            f.Token, _ => host.AskVersionAsync(model =>
            {
                displayed = model;
                throw new IOException("Native presentation unavailable");
            }, f.Token));
        await failure.Should().ThrowAsync<IOException>();
        displayed!.HasPresentation.Should().BeFalse();
        (await host.ReturnToCurrentQuestionAsync(f.Token)).Should().BeNull();
        clock.Timer.IsDisposed.Should().BeTrue();
        using var cancellation = new CancellationTokenSource();
        var shown = new TaskCompletionSource<NativeQuestionViewModel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = CreateQuery(f, logs).RunAsync(RequestOrigin.LocalUi, () => Task.CompletedTask,
            cancellation.Token, _ => host.AskVersionAsync(model =>
            {
                shown.SetResult(model);
                return Task.CompletedTask;
            }, cancellation.Token));
        displayed = await shown.Task;
        displayed.Dismiss();
        await cancellation.CancelAsync();
        var cancelled = () => run;
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        displayed.HasPresentation.Should().BeFalse();
        (await host.ReturnToCurrentQuestionAsync(f.Token)).Should().BeNull();
        clock.Timer.IsDisposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_pending_presentation_callback_cannot_keep_the_original_host_wait_alive_after_deadline_or_disposal(bool dispose)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var clock = new RecoveryClock(f.Time);
        using var host = CreateHost(f, clock);
        using var logs = LoggerFactory.Create(_ => { });
        var shown = new TaskCompletionSource<NativeQuestionViewModel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? presentation = null;
        var run = CreateQuery(f, logs).RunAsync(RequestOrigin.LocalUi, () => throw new InvalidOperationException("Must not dispatch"),
            f.Token, _ => host.AskVersionAsync(model => presentation = PresentAsync(model), f.Token));
        async Task PresentAsync(NativeQuestionViewModel value)
        {
            shown.SetResult(value);
            await release.Task;
            value.Edit(new(["show"]));
            await value.SubmitAsync();
        }
        var model = await shown.Task;
        model.Dismiss();
        if (dispose) { host.Dispose(); }
        else
        {
            clock.Advance(TimeSpan.FromMinutes(5));
            clock.Timers[0].Tick();
        }
        try
        {
            var finish = () => run;
            await finish.Should().ThrowAsync<Exception>();
            model.Completion.IsCompleted.Should().BeTrue();
            model.HasPresentation.Should().BeFalse();
            (await host.ReturnToCurrentQuestionAsync(f.Token)).Should().BeNull();
            clock.Timers.Should().OnlyContain(timer => timer.IsDisposed);
        }
        finally { release.TrySetResult(); }
        await presentation!;
        model.Answer.Choices.Should().BeEmpty();
        (await host.ReturnToCurrentQuestionAsync(f.Token)).Should().BeNull();
    }

    [Theory]
    [InlineData("presented")]
    [InlineData("dismissed")]
    [InlineData("restored")]
    public async Task CommittedCancellationPublishesItsReceiptBeforeConcurrentOriginalContinuationAndWithdrawal(string presentation)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var clock = new RecoveryClock(f.Time);
        using var host = CreateHost(f, clock);
        var controls = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        host.BindWorkspace(controls);
        using var logs = LoggerFactory.Create(_ => { });
        var shown = new TaskCompletionSource<NativeQuestionViewModel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatches = 0;
        var run = CreateQuery(f, logs).RunAsync(RequestOrigin.LocalUi, () =>
        {
            dispatches++;
            return Task.CompletedTask;
        }, f.Token, _ => host.AskVersionAsync(model =>
        {
            shown.SetResult(model);
            return Task.CompletedTask;
        }, f.Token));
        var model = await shown.Task;
        if (!string.Equals(presentation, "presented", StringComparison.Ordinal))
        {
            model.Edit(new(["show"]));
            model.Dismiss();
            if (string.Equals(presentation, "restored", StringComparison.Ordinal))
            {
                (await host.ReturnToCurrentQuestionAsync(f.Token)).Should().BeSameAs(model);
                model.Answer.Choices.Should().BeEmpty();
            }
        }
        var key = model.Key;
        using var relayPublished = new ManualResetEventSlim();
        using var releaseRelay = new ManualResetEventSlim();
        var blocked = 0;
        void Changed(object? sender, PropertyChangedEventArgs args)
        {
            if (!model.Completion.IsCompleted || model.Question.Status != QuestionStatus.Cancelled
                || Interlocked.Exchange(ref blocked, 1) != 0) { return; }
            relayPublished.Set();
            if (!releaseRelay.Wait(TimeSpan.FromSeconds(5), f.Token))
            {
                throw new TimeoutException("Committed cancellation notification was not released.");
            }
        }
        void Committed(HostTaskObservation observation)
        {
            if (observation.Task.Request != key.Request) { return; }
            // The existing host subscriber has queued the external relay. Hold the original
            // CancelAsync receipt until that relay publishes, then race its continuation with withdrawal.
            if (!relayPublished.Wait(TimeSpan.FromSeconds(5), f.Token))
            {
                throw new TimeoutException("The external cancellation relay did not publish.");
            }
        }
        model.PropertyChanged += Changed;
        controls.WaitingTaskCancelled += Committed;
        try
        {
            var operation = string.Equals(presentation, "dismissed", StringComparison.Ordinal)
                ? controls.CancelTaskAsync(new(key.Request.SessionId, key.Request.TaskId, new(1),
                    model.Question.SessionGeneration, key.QuestionId, key.Revision), RequestOrigin.LocalUi, () => true, f.Token)
                : model.CancelAsync();
            await operation.WaitAsync(TimeSpan.FromSeconds(5), f.Token);
            relayPublished.IsSet.Should().BeTrue();
            (await model.Completion).Outcome.Should().Be(HostInteractionOutcome.Cancelled);
            // A different thread must be able to clear presentation while Notify is blocked.
            await Task.Run(() => model.Close("Private presentation withdrawn after the committed receipt."), f.Token)
                .WaitAsync(TimeSpan.FromSeconds(5), f.Token);
            model.Fail(new IOException("Late continuation failure cannot replace the committed receipt."));
            (await model.Completion).Outcome.Should().Be(HostInteractionOutcome.Cancelled);
            model.IsWithdrawn.Should().BeTrue();
            model.HasPresentation.Should().BeFalse();
            model.Answer.Choices.Should().BeEmpty();
            model.ReviewText.Should().BeEmpty();
            var stored = (await f.Store.ReadTaskAsync(key.Request.SessionId, key.Request.TaskId, f.Token))!;
            stored.Task.State.Should().Be(HostTaskState.Cancelled);
            stored.Task.Revision.Value.Should().Be(2);
            stored.Question!.Status.Should().Be(QuestionStatus.Cancelled);
            stored.Question.Key.Should().Be(key.Next());
            using var inspection = HostActivity.BeginRoot(key.Request, HostActivityLayer.Application, HostOperation.Request);
            var receipts = await controls.ReadTasksAsync(key.Request.SessionId, null, 25, f.Token);
            receipts.Records.Should().HaveCount(2);
            receipts.Records.Should().ContainSingle(task => task.Request == key.Request);
            receipts.Records.Should().ContainSingle(task => task.Request != key.Request && task.State == HostTaskState.Succeeded);
            using var authority = f.OpenRaw();
            using var evidence = authority.CreateCommand();
            evidence.CommandText = "SELECT count(*) FROM host_observations WHERE request_id=$request;";
            evidence.Parameters.AddWithValue("$request", key.Request.RequestId.Value.ToString("D"));
            evidence.ExecuteScalar().Should().Be(0L);
            evidence.CommandText = "SELECT count(*) FROM security_audit_events WHERE json_extract(envelope,'$.Audit.ActionId')='task.cancel.wait';";
            evidence.ExecuteScalar().Should().Be(1L);
        }
        finally
        {
            releaseRelay.Set();
            model.PropertyChanged -= Changed;
            controls.WaitingTaskCancelled -= Committed;
            await run.WaitAsync(TimeSpan.FromSeconds(5), f.Token);
        }
        (await run.WaitAsync(TimeSpan.FromSeconds(5), f.Token)).State.Should().Be(HostTaskState.Cancelled);
        dispatches.Should().Be(0);
        (await host.ReturnToCurrentQuestionAsync(f.Token)).Should().BeNull();
        clock.Timers.Should().OnlyContain(timer => timer.IsDisposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EarlierWithdrawalOrFailureCannotBeRelabelledByACommittedCancellation(bool fail)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var question = await WindowsSqliteTaskControlTests.WaitAsync(f);
        using var host = CreateHost(f, new RecoveryClock(f.Time));
        var controls = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        var published = new TaskCompletionSource<HostTaskObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = host.CreateState(question, async key =>
        {
            var cancelled = await controls.CancelTaskAsync(new(key.Request.SessionId, key.Request.TaskId,
                new(1), question.SessionGeneration, key.QuestionId, key.Revision), RequestOrigin.LocalUi, () => true, f.Token);
            published.SetResult(cancelled);
            await release.Task;
            return new(HostInteractionOutcome.Cancelled, "pre-dispatch-work-cancelled", cancelled.Question);
        });
        var operation = model.CancelAsync();
        var committed = await published.Task.WaitAsync(TimeSpan.FromSeconds(5), f.Token);
        var failure = new IOException("Original authoritative failure");
        try
        {
            if (fail) { model.Fail(failure); }
            else { model.Close("Privacy/source/ownership withdrawn before terminal publication."); }
            model.AcceptCommittedCancellation(committed);
        }
        finally { release.SetResult(); }
        await operation;
        if (fail)
        {
            var finish = () => model.Completion;
            (await finish.Should().ThrowAsync<IOException>()).Which.Should().BeSameAs(failure);
        }
        else { (await model.Completion).Outcome.Should().Be(HostInteractionOutcome.Denied); }
        (await f.Store.ReadTaskAsync(question.Key.Request.SessionId, question.Key.Request.TaskId, f.Token))!
            .Task.State.Should().Be(HostTaskState.Cancelled);
        model.IsWithdrawn.Should().BeTrue();
        model.HasPresentation.Should().BeFalse();
        model.Answer.Choices.Should().BeEmpty();
        await model.RestorePresentationAsync();
        model.HasPresentation.Should().BeFalse();
    }

    [Theory]
    [InlineData("request")]
    [InlineData("question")]
    [InlineData("revision")]
    [InlineData("generation")]
    [InlineData("question-generation")]
    [InlineData("status")]
    [InlineData("privacy")]
    public async Task ForeignOrIneligibleCancellationCannotAcquireTerminalPublication(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var question = await WindowsSqliteTaskControlTests.WaitAsync(f);
        var eligible = true;
        using var host = CreateHost(f, new RecoveryClock(f.Time), () => eligible);
        var model = host.CreateState(question);
        var controls = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        var committed = await controls.CancelTaskAsync(new(question.Key.Request.SessionId, question.Key.Request.TaskId,
            new(1), question.SessionGeneration, question.Key.QuestionId, question.Key.Revision),
            RequestOrigin.LocalUi, () => true, f.Token);
        var cancelled = committed.Question!;
        var invalid = change switch
        {
            "request" => committed with { Task = new(HostRequest.Create(RequestOrigin.LocalUi), committed.Task.Revision, committed.Task.State) },
            "question" => committed with { Question = cancelled with { Key = new(cancelled.Key.Request, new(Guid.NewGuid()), cancelled.Key.Revision) } },
            "revision" => committed with { Question = cancelled with { Key = question.Key } },
            "generation" => committed with { Generation = new(2) },
            "question-generation" => committed with { Question = cancelled with { SessionGeneration = new(2) } },
            "status" => committed with { Question = cancelled with { Status = QuestionStatus.Answered } },
            _ => committed,
        };
        if (string.Equals(change, "privacy", StringComparison.Ordinal)) { eligible = false; }
        model.AcceptCommittedCancellation(invalid);
        model.Completion.IsCompleted.Should().BeFalse();
        model.Question.Should().Be(question);
        model.Close("End the refused presentation without dispatch.");
        model.AcceptCommittedCancellation(committed);
        (await model.Completion).Outcome.Should().Be(HostInteractionOutcome.Denied);
    }

    [WindowsFact]
    public async Task DismissalDuringRestoreCannotRepublishSavedPrivateControls()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        using var host = CreateHost(f, new RecoveryClock(f.Time));
        using var logs = LoggerFactory.Create(_ => { });
        var shown = new TaskCompletionSource<NativeQuestionViewModel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = CreateQuery(f, logs).RunAsync(RequestOrigin.LocalUi, () => throw new InvalidOperationException("Must not dispatch"),
            f.Token, _ => host.AskVersionAsync(model =>
            {
                shown.SetResult(model);
                return Task.CompletedTask;
            }, f.Token));
        var model = await shown.Task;
        model.Edit(new(["show"]));
        await model.SaveDraftAsync();
        model.Dismiss();
        var reviewing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        checkpoint.Commit = (_, _) =>
        {
            reviewing.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5), f.Token)) { throw new TimeoutException("Restore was not released."); }
        };
        var restoring = model.RestorePresentationAsync();
        try
        {
            await reviewing.Task.WaitAsync(TimeSpan.FromSeconds(5), f.Token);
            model.Dismiss();
        }
        finally
        {
            checkpoint.Commit = null;
            release.Set();
        }
        await restoring;
        model.HasPresentation.Should().BeFalse();
        model.Answer.Choices.Should().BeEmpty();
        model.ReviewText.Should().BeEmpty();
        model.Completion.IsCompleted.Should().BeFalse();
        (await host.ReturnToCurrentQuestionAsync(f.Token)).Should().BeSameAs(model);
        model.Answer.Choices.Should().Equal("show");
        await model.CancelAsync();
        (await run).State.Should().Be(HostTaskState.Cancelled);
    }

    private static NativeQuestionHost CreateHost(InteractionStorageFixture f, TimeProvider time, Func<bool>? gate = null)
    {
        var host = new NativeQuestionHost(f.Store, time, NullLogger<NativeQuestionViewModel>.Instance);
        host.BindGate(gate ?? (static () => true));
        host.BindAdmissionRevision(static () => 1);
        host.BindWorkspace(WindowsSqliteSessionWorkspaceTests.Service(f, new()));
        return host;
    }

    private static DurableVersionQuery CreateQuery(InteractionStorageFixture f, ILoggerFactory logs) =>
        new(new(f.Tasks), new LoggerSecurityAuditLog(logs.CreateLogger<LoggerSecurityAuditLog>()),
            logs.CreateLogger<DurableVersionQuery>());

    private static async Task ShowQuestionAsync(QuestionWindow window, NativeQuestionViewModel model)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, PropertyChangedEventArgs args)
        {
            if (window.IsActive && model.IsEditable) { ready.TrySetResult(); }
        }
        model.PropertyChanged += Changed;
        try
        {
            window.Show();
            window.Activate();
            if (window.IsActive && model.IsEditable) { ready.TrySetResult(); }
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally { model.PropertyChanged -= Changed; }
    }

    private sealed class RecoveryClock(InteractionStorageFixture.Clock clock) : TimeProvider
    {
        internal PrivacyPollingTimeProvider.PollTimer Timer { get; private set; } = null!;
        internal List<PrivacyPollingTimeProvider.PollTimer> Timers { get; } = [];
        public override DateTimeOffset GetUtcNow() => clock.GetUtcNow();
        public override long GetTimestamp() => clock.GetTimestamp();
        public override long TimestampFrequency => clock.TimestampFrequency;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Timer = new(callback, state, dueTime, period);
            Timers.Add(Timer);
            return Timer;
        }

        internal void Advance(TimeSpan elapsed)
        {
            clock.Now += elapsed;
            clock.MonotonicTicks += elapsed.Ticks;
        }
    }
}
