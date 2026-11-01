using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Skills;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Skills;
using Kora.Definitions.Skills;
using Kora.NativeUxFixture;
using Kora.Tools.SessionControl;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Skills;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task BundledNativeSourceButtonUsesActualControllerWithFreshOrExplicitlyAddressedControlSessions(
        bool qualified, bool nativeFailure)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var storage = new InteractionStorageFixture();
            await storage.Tasks.InitializeAsync(storage.Token);
            await storage.Store.InitializeAsync(storage.Token);
            using var native = new NativeUxFixtureSession(storage.Paths.LocalRoot);
            await native.InitializeAsync();
            await using var composition = new Composition(storage);
            using var questions = new NativeQuestionHost(storage.Store, storage.Time, NullLogger<NativeQuestionViewModel>.Instance);
            questions.BindGate(() => native.Main.CanRevealPrivatePresentation);
            questions.BindAdmissionRevision(() => 1);
            questions.BindPrivateRevision(() => composition.Access.ControlRevision);
            questions.BindCallObservation(() => new(1, CallState.Clear, false, CallAwareSettings.Default));
            var renderer = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance);
            using var sources = new SkillPackagesWindowController(native.Main, () => true,
                NullLogger<SkillPackagesWindowController>.Instance, renderer, native.SharedSkills,
                NullLogger<SharedSkillSourcesWindowController>.Instance);
            var owner = new Window();
            owner.Show();
            using var lifecycle = new ApplicationLifecycleController(native.Main, composition.Conversation, native.Queue,
                questions, new(storage.Tasks), renderer, () => owner);
            var platform = new BundledNativePlatform
            { LockOutcome = nativeFailure ? FixedSessionEffectOutcome.Failed : FixedSessionEffectOutcome.RequestAccepted };
            var worker = new BundledNativeWorker();
            using var controller = new BundledFixedActionController(native.Main, sources, questions, lifecycle,
                storage.Store, new(storage.Tasks), storage.Authorization,
                qualified ? worker : new WindowsBundledScriptWorker(),
                new(new WindowsFixedSessionEffect(platform), new BundledNativeAudit(), NullLogger<FixedSessionActionBroker>.Instance),
                renderer, () => owner, () => true, () => 1, NullLogger<BundledFixedActionController>.Instance);
            SkillPackagesWindow? inspector = null;
            var openedQuestions = 0;
            var questionOpened = new TaskCompletionSource<QuestionWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var inspect = Window.WindowOpenedEvent.AddClassHandler<SkillPackagesWindow>((window, _) => inspector = window);
            using var observe = Window.WindowOpenedEvent.AddClassHandler<QuestionWindow>((window, _) =>
            { openedQuestions++; questionOpened.TrySetResult(window); });
            try
            {
                sources.OpenPackage("kora.session.lock", () => true).Should().BeTrue();
                var view = inspector ?? throw new InvalidOperationException("The actual package inspector did not open.");
                for (var index = 0; index < (qualified ? 3 : 1); index++)
                {
                    questionOpened = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var button = view.FindControl<Button>(index == 2 ? "ReviewBoundBundledExecution" : "ReviewBundledExecution")!;
                    button.IsEnabled.Should().BeTrue();
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (qualified && index < 2)
                    {
                        if (await Task.WhenAny(questionOpened.Task, controller.ActiveOperation) == controller.ActiveOperation)
                        { await controller.ActiveOperation; questionOpened.Task.IsCompleted.Should().BeTrue(controller.Status); }
                        var question = await questionOpened.Task.WaitAsync(storage.Token);
                        await BundledButtonEnabled(question.FindControl<Button>("ReviewQuestion")!).WaitAsync(storage.Token);
                        question.GetLogicalDescendants().OfType<RadioButton>().Single(item => item.Content is "This session").IsChecked = true;
                        question.FindControl<Button>("ReviewQuestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await BundledButtonEnabled(question.FindControl<Button>("SubmitAnswer")!).WaitAsync(storage.Token);
                        question.FindControl<Button>("SubmitAnswer")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    await controller.ActiveOperation;
                    controller.Status.Should().Contain(!qualified ? "not qualified"
                        : index == 2 && !nativeFailure ? "nonterminal/Unknown"
                        : nativeFailure ? "Windows rejected" : "Windows accepted");
                }
                openedQuestions.Should().Be(qualified ? 2 : 0);
                platform.Requests.Should().HaveCount(qualified ? nativeFailure ? 3 : 2 : 0);
                if (qualified)
                {
                    worker.Invocations[0].SessionId.Should().NotBe(worker.Invocations[1].SessionId);
                    if (nativeFailure)
                    { worker.Invocations[2].SessionId.Should().Be(worker.Invocations[1].SessionId); }
                    worker.Invocations.Select(item => item.InvocationId).Distinct().Should().HaveCount(nativeFailure ? 3 : 2);
                }
            }
            finally { owner.Close(); }
        });
    }

    private static Task BundledButtonEnabled(Button button)
    {
        if (button.IsEnabled) { return Task.CompletedTask; }
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        button.PropertyChanged += (_, args) =>
        { if (args.Property == Avalonia.Input.InputElement.IsEnabledProperty && button.IsEnabled) { ready.TrySetResult(); } };
        return ready.Task;
    }

    [Theory]
    [InlineData(FixedSessionAction.Shutdown, "accepted")]
    [InlineData(FixedSessionAction.Restart, "accepted")]
    [InlineData(FixedSessionAction.Shutdown, "cancelled")]
    [InlineData(FixedSessionAction.Restart, "cancelled")]
    [InlineData(FixedSessionAction.Shutdown, "failed")]
    [InlineData(FixedSessionAction.Restart, "failed")]
    [InlineData(FixedSessionAction.Shutdown, "unavailable")]
    [InlineData(FixedSessionAction.Restart, "unavailable")]
    public async Task BundledOriginalTypedPowerRequestUsesComposedControllerAndTruthfulTerminal(
        FixedSessionAction action, string outcome)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var storage = new InteractionStorageFixture();
            await storage.Tasks.InitializeAsync(storage.Token);
            await storage.Store.InitializeAsync(storage.Token);
            using var native = new NativeUxFixtureSession(storage.Paths.LocalRoot);
            await native.InitializeAsync();
            await using var composition = new Composition(storage);
            using var questions = new NativeQuestionHost(storage.Store, storage.Time, NullLogger<NativeQuestionViewModel>.Instance);
            questions.BindGate(() => native.Main.CanRevealPrivatePresentation);
            questions.BindAdmissionRevision(() => 1);
            questions.BindPrivateRevision(() => composition.Access.ControlRevision);
            questions.BindCallObservation(() => new(1, CallState.Clear, false, CallAwareSettings.Default));
            var renderer = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance);
            using var sources = new SkillPackagesWindowController(native.Main, () => true,
                NullLogger<SkillPackagesWindowController>.Instance, renderer, native.SharedSkills,
                NullLogger<SharedSkillSourcesWindowController>.Instance);
            var owner = new Window();
            owner.Show();
            var clock = new BundledCountdownClock(storage.Time.GetUtcNow());
            using var lifecycle = new ApplicationLifecycleController(native.Main, composition.Conversation, native.Queue,
                questions, new(storage.Tasks), renderer, () => owner, clock);
            var platform = new BundledNativePlatform
            { PowerOutcome = outcome is "failed" ? FixedSessionEffectOutcome.Failed : FixedSessionEffectOutcome.RequestAccepted };
            var audit = new BundledNativeAudit();
            var worker = new BundledNativeWorker();
            using var controller = new BundledFixedActionController(native.Main, sources, questions, lifecycle,
                storage.Store, new(storage.Tasks), storage.Authorization,
                outcome is "unavailable" ? new WindowsBundledScriptWorker() : worker,
                new(new WindowsFixedSessionEffect(platform), audit, NullLogger<FixedSessionActionBroker>.Instance),
                renderer, () => owner, () => true, () => 1, NullLogger<BundledFixedActionController>.Instance);
            var grantQuestion = new TaskCompletionSource<QuestionWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            var powerQuestion = new TaskCompletionSource<QuestionWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var observe = Window.WindowOpenedEvent.AddClassHandler<QuestionWindow>((window, _) =>
            {
                if (window.GetLogicalDescendants().OfType<RadioButton>().Any(button => button.Content is "Once"))
                { grantQuestion.TrySetResult(window); }
                else { powerQuestion.TrySetResult(window); }
            });
            try
            {
                native.Main.CommandText = action == FixedSessionAction.Shutdown
                    ? "shut down the computer" : "restart the computer";
                var running = native.Main.RunTypedCommand.ExecuteAsync();
                if (outcome is not "unavailable")
                {
                    if (await Task.WhenAny(grantQuestion.Task, running) == running)
                    {
                        await running;
                        grantQuestion.Task.IsCompleted.Should().BeTrue(controller.Status + "\n" + native.Main.ResponseBody);
                    }
                    var grant = await grantQuestion.Task.WaitAsync(storage.Token);
                    await BundledButtonEnabled(grant.FindControl<Button>("ReviewQuestion")!).WaitAsync(storage.Token);
                    grant.GetLogicalDescendants().OfType<RadioButton>().Single(button => button.Content is "Once").IsChecked = true;
                    grant.FindControl<Button>("ReviewQuestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await BundledButtonEnabled(grant.FindControl<Button>("SubmitAnswer")!).WaitAsync(storage.Token);
                    platform.Requests.Should().BeEmpty();
                    grant.FindControl<Button>("SubmitAnswer")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (await Task.WhenAny(powerQuestion.Task, running) == running)
                    {
                        await running;
                        powerQuestion.Task.IsCompleted.Should().BeTrue(controller.Status);
                    }
                    var power = await powerQuestion.Task.WaitAsync(storage.Token);
                    await BundledButtonEnabled(power.FindControl<Button>("CancelQuestion")!).WaitAsync(storage.Token);
                    worker.Calls.Should().Be(1);
                    platform.Requests.Should().BeEmpty();
                    if (outcome is "cancelled")
                    { power.FindControl<Button>("CancelQuestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                    else
                    {
                        power.GetLogicalDescendants().OfType<RadioButton>().Single(button =>
                            button.Content is "Confirm shutdown" or "Confirm computer restart").IsChecked = true;
                        await BundledButtonEnabled(power.FindControl<Button>("SubmitAnswer")!).WaitAsync(storage.Token);
                        power.FindControl<Button>("SubmitAnswer")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if (await Task.WhenAny(clock.Started.Task, running) == running) { await running; }
                        await clock.Started.Task.WaitAsync(storage.Token);
                        platform.Requests.Should().BeEmpty();
                        clock.Advance(TimeSpan.FromSeconds(30));
                    }
                }
                await running;
                await controller.ActiveOperation;
                if (outcome is "unavailable")
                {
                    controller.Status.Should().Contain("not qualified");
                    worker.Calls.Should().Be(0);
                    grantQuestion.Task.IsCompleted.Should().BeFalse();
                    powerQuestion.Task.IsCompleted.Should().BeFalse();
                }
                else
                {
                    var invocation = worker.Invocations.Single();
                    var terminal = await storage.Store.ReadTaskAsync(invocation.SessionId, invocation.TaskId, storage.Token);
                    terminal!.Task.State.Should().Be(outcome is "accepted" ? HostTaskState.Unknown : HostTaskState.Failed);
                    controller.Status.Should().Contain(outcome is "accepted" ? "Windows accepted"
                        : outcome is "failed" ? "Windows rejected" : "did not reach a native effect");
                }
                platform.Requests.Should().Equal(outcome is "accepted" or "failed" ? [action] : []);
                platform.Restorations.Should().Be(platform.Activations);
                audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
                worker.IsQuiescent.Should().BeTrue();
            }
            finally { owner.Close(); }
        });
    }

    [Theory]
    [InlineData("once", false, RequestOrigin.LocalUi, 2)]
    [InlineData("session", false, RequestOrigin.LocalUi, 1)]
    [InlineData("session", true, RequestOrigin.LocalUi, 2)]
    [InlineData("perpetual", true, RequestOrigin.LocalUi, 1)]
    [InlineData("once", false, RequestOrigin.ActivatedVoice, 2)]
    [InlineData("session", false, RequestOrigin.ActivatedVoice, 1)]
    public async Task BundledNativePermissionRetainsExactScopesAndNativeGrantExceptionWithoutExecuting(
        string scope, bool anotherSession, RequestOrigin origin, int expectedQuestions)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var storage = new InteractionStorageFixture();
            await storage.Tasks.InitializeAsync(storage.Token);
            await storage.Store.InitializeAsync(storage.Token);
            using var host = new NativeQuestionHost(storage.Store, storage.Time, NullLogger<NativeQuestionViewModel>.Instance);
            host.BindGate(() => true);
            host.BindAdmissionRevision(() => 1);
            host.BindPrivateRevision(() => 1);
            host.BindCallObservation(() => new(1, CallState.Clear, false, CallAwareSettings.Default));
            var script = EmbeddedFixedActionCatalogue.Resolve(EmbeddedSkillCatalogue.Load().Packages.Single(package =>
                string.Equals(package.Manifest.Id, "kora.session.lock", StringComparison.Ordinal)));
            var binding = BundledActionBinding.Create(script, new string('a', 64), new string('b', 64));
            var renderer = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance);
            WorkSessionAuthorization? session = null;
            var presented = 0;
            for (var index = 0; index < 2; index++)
            {
                var control = new HostRequest(new(Guid.NewGuid()),
                    session is null || anotherSession ? new(Guid.NewGuid()) : session.SessionId, new(Guid.NewGuid()), origin);
                using var original = HostActivity.BeginRoot(control, HostActivityLayer.Desktop, HostOperation.Request);
                await storage.Store.RecordControlIntentAsync(control, storage.Token);
                if (session is null || anotherSession)
                { session = await storage.Store.CreateBundledControlSessionAsync(control, () => true, storage.Token); }
                var dispatched = await storage.Store.AdmitBundledControlDispatchAsync(
                    control, session.Generation, () => true, storage.Token);
                var invocation = new HostRequest(control.RequestId, control.SessionId, control.TaskId, origin, new(Guid.NewGuid()));
                var proposal = new HostOperationProposal(invocation, new(Guid.NewGuid()), new(1), binding,
                    HostOperationEffect.BoundedWrite, storage.Time.GetUtcNow().AddSeconds(30));
                using (var executing = HostActivity.BeginRoot(invocation, HostActivityLayer.Desktop, HostOperation.Tool))
                {
                    var decision = await host.RequestBundledPermissionAsync(proposal, script, binding,
                        session.Generation, () => true, async state =>
                        {
                            presented++;
                            state.IsApproval.Should().BeTrue();
                            state.CanAnswerByVoice.Should().BeFalse();
                            var window = new QuestionWindow(state, renderer);
                            window.Show();
                            try
                            {
                                state.Edit(new([scope]));
                                await state.ReviewAsync();
                                state.CanSubmit.Should().BeTrue();
                                await state.SubmitAsync();
                                state.Completion.IsCompleted.Should().BeTrue();
                            }
                            finally { window.Close(); }
                        }, storage.Token);
                    decision.Outcome.Should().Be(HostInteractionOutcome.Consumed);
                    decision.Admission.Should().NotBeNull();
                    decision.Admission!.Dispatched.Should().BeFalse();
                    decision.Admission.Proposal.Should().Be(proposal);
                }
                await new HostTaskCoordinator(storage.Tasks).RecordOutcomeAsync(dispatched, HostTaskState.Failed, storage.Token);
            }
            presented.Should().Be(expectedQuestions);
            var grants = await storage.Store.ReadGrantsAsync(storage.Token);
            grants.Should().OnlyContain(grant => grant.CreatorChannel == RequestOrigin.LocalUi);
        });
    }

    [Theory]
    [InlineData(FixedSessionAction.Lock, true, true, false)]
    [InlineData(FixedSessionAction.Lock, false, false, false)]
    [InlineData(FixedSessionAction.Shutdown, true, true, false)]
    [InlineData(FixedSessionAction.Shutdown, true, true, true)]
    [InlineData(FixedSessionAction.Shutdown, true, false, false)]
    [InlineData(FixedSessionAction.Shutdown, false, false, false)]
    [InlineData(FixedSessionAction.Restart, true, true, false)]
    [InlineData(FixedSessionAction.Restart, true, true, true)]
    [InlineData(FixedSessionAction.Restart, true, false, false)]
    [InlineData(FixedSessionAction.Restart, false, false, false)]
    public async Task BundledNativeGrantAndDistinctPowerConsentReachRealWindowsBindingOnlyThroughJoinedHost(
        FixedSessionAction action, bool grant, bool powerConsent, bool cancelCountdown)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var storage = new InteractionStorageFixture();
            await storage.Tasks.InitializeAsync(storage.Token);
            await storage.Store.InitializeAsync(storage.Token);
            using var native = new NativeUxFixtureSession(storage.Paths.LocalRoot);
            await native.InitializeAsync();
            await using var composition = new Composition(storage);
            using var questionHost = new NativeQuestionHost(storage.Store, storage.Time, NullLogger<NativeQuestionViewModel>.Instance);
            questionHost.BindGate(() => native.Main.CanRevealPrivatePresentation);
            questionHost.BindAdmissionRevision(() => 1);
            questionHost.BindPrivateRevision(() => composition.Access.ControlRevision);
            questionHost.BindCallObservation(() => new(1, CallState.Clear, false, CallAwareSettings.Default));
            var owner = new Window();
            owner.Show();
            var clock = new BundledCountdownClock(storage.Time.GetUtcNow());
            using var observeCountdown = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window.Title is "Computer shutdown countdown" or "Computer restart countdown")
                { clock.CountdownWindow = window; }
            });
            using var lifecycle = new ApplicationLifecycleController(native.Main, composition.Conversation, native.Queue,
                questionHost, new(storage.Tasks), new(NullLogger<NativeDetailRenderer>.Instance), () => owner, clock);
            var packageId = action switch
            {
                FixedSessionAction.Lock => "kora.session.lock",
                FixedSessionAction.Shutdown => "kora.computer.shutdown",
                _ => "kora.computer.restart",
            };
            var catalogue = EmbeddedSkillCatalogue.Load();
            var script = EmbeddedFixedActionCatalogue.Resolve(catalogue.Packages.Single(package =>
                string.Equals(package.Manifest.Id, packageId, StringComparison.Ordinal)));
            var renderer = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance);
            var inspector = new SkillPackagesWindow(catalogue, renderer, current: () => true);
            inspector.Show(owner);
            inspector.SelectPackage(packageId).Should().BeTrue();
            var platform = new BundledNativePlatform();
            var effect = new WindowsFixedSessionEffect(platform);
            var audit = new BundledNativeAudit();
            var broker = new FixedSessionActionBroker(effect, audit, NullLogger<FixedSessionActionBroker>.Instance);
            var worker = new BundledNativeWorker();
            var root = HostRequest.Create(RequestOrigin.LocalUi);
            var request = new HostRequest(root.RequestId, root.SessionId, root.TaskId, root.Origin, new(Guid.NewGuid()));
            using var activity = HostActivity.BeginRoot(root, HostActivityLayer.Desktop, HostOperation.Request);
            var resumeOriginal = HostActivity.CaptureContinuation(HostActivityLayer.Desktop, HostOperation.Request);
            await storage.Store.RecordControlIntentAsync(root, storage.Token);
            var session = await storage.Store.CreateBundledControlSessionAsync(root, () => true, storage.Token);
            var intent = await storage.Store.AdmitBundledControlDispatchAsync(root, session.Generation, () => true, storage.Token);
            using var executing = HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Tool);
            var presented = 0;
            var powerQuestion = new TaskCompletionSource<QuestionWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var observe = Window.WindowOpenedEvent.AddClassHandler<QuestionWindow>((window, _) =>
            {
                if (window.GetLogicalDescendants().OfType<RadioButton>().Any(button =>
                    button.Content is "Confirm shutdown" or "Confirm computer restart"))
                { powerQuestion.TrySetResult(window); }
            });
            var invocation = new BundledFixedActionInvocation(script, worker, broker, storage.Authorization,
                async (proposal, generation, current, token) => await Dispatcher.UIThread.InvokeAsync(async () =>
                    await questionHost.RequestBundledPermissionAsync(proposal, script,
                        BundledActionBinding.Create(script, worker.QualifiedImplementationDigest!, broker.CurrentIdentityDigest!),
                        generation, current, async state =>
                        {
                            presented++;
                            state.Question.Spec.SourceId.Should().Be("bundled-fixed-script");
                            state.Question.Spec.Text.Should().Contain("shared helper").And.Contain("physical");
                            var window = new QuestionWindow(state, renderer);
                            window.Show(inspector);
                            try
                            {
                                platform.Requests.Should().BeEmpty();
                                await BundledButtonEnabled(window.FindControl<Button>("ReviewQuestion")!).WaitAsync(token);
                                if (grant)
                                {
                                    state.Edit(new(["once"]));
                                    await state.ReviewAsync();
                                    await state.SubmitAsync();
                                }
                                else { await state.CancelAsync(); }
                            }
                            finally { window.Close(); }
                        }, token)),
                () => EmbeddedFixedActionCatalogue.Resolve(EmbeddedSkillCatalogue.Load().Packages.Single(package =>
                    string.Equals(package.Manifest.Id, packageId, StringComparison.Ordinal))),
                request, session.Generation, () => native.Main.CanRevealPrivatePresentation,
                async token => await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    using var original = resumeOriginal();
                    var prepared = await native.Main.PrepareBundledResourcesAsync(action, () => true,
                        current => lifecycle.PrepareComputerPowerAsync(action == FixedSessionAction.Shutdown
                            ? ApplicationLifecycleOperation.ComputerShutdown : ApplicationLifecycleOperation.ComputerRestart,
                            request, current, token));
                    return prepared is null ? null : new ApplicationLifecycleShutdown(() => prepared.IsCurrent,
                        async () => await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            using var joining = resumeOriginal();
                            await prepared.StopAndJoinAsync();
                        }),
                        () => Dispatcher.UIThread.Invoke(prepared.Dispose));
                }), storage.Time);
            try
            {
                var running = invocation.ExecuteAsync(storage.Token);
                if (!grant)
                {
                    await ((Func<Task>)(async () => await running)).Should().ThrowAsync<InvalidOperationException>();
                    worker.Calls.Should().Be(0);
                }
                else
                {
                    if (action != FixedSessionAction.Lock)
                    {
                        if (await Task.WhenAny(powerQuestion.Task, running) == running) { await running; }
                        var question = await powerQuestion.Task.WaitAsync(storage.Token);
                        worker.Calls.Should().Be(1);
                        platform.Requests.Should().BeEmpty();
                        var cancel = question.FindControl<Button>("CancelQuestion")!;
                        await BundledButtonEnabled(cancel).WaitAsync(storage.Token);
                        if (!powerConsent)
                        { cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                        else
                        {
                            question.GetLogicalDescendants().OfType<RadioButton>().Single(button =>
                                button.Content is "Confirm shutdown" or "Confirm computer restart").IsChecked = true;
                            var submit = question.FindControl<Button>("SubmitAnswer")!;
                            await BundledButtonEnabled(submit).WaitAsync(storage.Token);
                            submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            if (await Task.WhenAny(clock.Started.Task, running) == running) { await running; }
                            await clock.Started.Task.WaitAsync(storage.Token);
                            platform.Requests.Should().BeEmpty();
                            clock.Advance(TimeSpan.FromSeconds(29));
                            platform.Requests.Should().BeEmpty();
                            running.IsCompleted.Should().BeFalse();
                            if (cancelCountdown)
                            {
                                clock.CancelCountdown();
                            }
                            else { clock.Advance(TimeSpan.FromSeconds(1)); }
                        }
                    }
                    if (cancelCountdown)
                    { await ((Func<Task>)(async () => await running)).Should().ThrowAsync<OperationCanceledException>(); }
                    else if (action != FixedSessionAction.Lock && !powerConsent)
                    { await ((Func<Task>)(async () => await running)).Should().ThrowAsync<InvalidOperationException>(); }
                    else
                    {
                        var result = await running;
                        result.Outcome.Should().Be(FixedSessionEffectOutcome.RequestAccepted);
                        result.AuditConfirmed.Should().BeTrue();
                    }
                }
                platform.Requests.Should().HaveCount(grant && (action == FixedSessionAction.Lock || powerConsent && !cancelCountdown) ? 1 : 0);
                platform.Activations.Should().Be(grant && powerConsent && !cancelCountdown && action != FixedSessionAction.Lock ? 1 : 0);
                platform.Restorations.Should().Be(platform.Activations);
                presented.Should().Be(1);
                audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
                var task = await storage.Store.ReadTaskAsync(request.SessionId, request.TaskId, storage.Token);
                task!.Task.Should().Be(intent);
            }

            finally { inspector.Close(); owner.Close(); }
        });
    }

    private sealed class BundledCountdownClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset instant = now;
        private TimerCallback? callback;
        private object? state;
        private DateTimeOffset due;
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Window? CountdownWindow { get; set; }
        public override DateTimeOffset GetUtcNow() => instant;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            dueTime.Should().Be(TimeSpan.FromSeconds(30));
            this.callback = callback;
            this.state = state;
            due = instant + dueTime;
            Started.SetResult();
            return new CountdownTimer();
        }
        internal void Advance(TimeSpan age)
        {
            instant += age;
            if (instant >= due) { callback!(state); }
        }
        internal void CancelCountdown() => CountdownWindow!.GetLogicalDescendants().OfType<Button>().Single()
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        private sealed class CountdownTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new InvalidOperationException("Unexpected countdown reset.");
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class BundledNativeWorker : IBundledScriptWorker
    {
        public string QualifiedImplementationDigest { get; } = new string('a', 64);
        public bool IsQuiescent => true;
        internal int Calls { get; private set; }
        internal List<HostRequest> Invocations { get; } = [];
        public Task<BundledScriptReceipt> RunAsync(BundledFixedScript script, HostRequest invocation,
            Func<bool> current, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current().Should().BeTrue();
            script.Package.Manifest.HelperLoadOrder.Should().ContainSingle();
            Calls++;
            Invocations.Add(invocation);
            return Task.FromResult(new BundledScriptReceipt(invocation, script.Action,
                script.Package.DeclaredResourceDigest, true, true));
        }
    }

    private sealed class BundledNativePlatform : IWindowsFixedSessionPlatform, IWindowsShutdownAuthority
    {
        internal List<FixedSessionAction> Requests { get; } = [];
        internal FixedSessionEffectOutcome LockOutcome { get; init; } = FixedSessionEffectOutcome.RequestAccepted;
        internal FixedSessionEffectOutcome PowerOutcome { get; init; } = FixedSessionEffectOutcome.RequestAccepted;
        internal int Activations { get; private set; }
        internal int Restorations { get; private set; }
        public bool IsCurrentInteractiveSession() => true;
        public FixedSessionEffectResult Lock() { Requests.Add(FixedSessionAction.Lock); return new(LockOutcome, LockOutcome == FixedSessionEffectOutcome.Failed ? 5 : 0); }
        public IWindowsShutdownAuthority DuplicateShutdownAuthority() => this;
        public int Enable() { Activations++; return 0; }
        public FixedSessionEffectResult Request(FixedSessionAction action) { Requests.Add(action); return new(PowerOutcome, PowerOutcome == FixedSessionEffectOutcome.Failed ? 5 : 0); }
        public int Restore() { Restorations++; return 0; }
        public void Dispose() { }
    }

    private sealed class BundledNativeAudit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }
}