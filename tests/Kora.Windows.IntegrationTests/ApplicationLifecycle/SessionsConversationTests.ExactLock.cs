using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.NativeUxFixture;
using Kora.Tools.SessionControl;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Skills;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(SecurityAuditInitiator.TypedCommand, RequestOrigin.LocalUi, true, true)]
    [InlineData(SecurityAuditInitiator.LocalUser, RequestOrigin.LocalUi, true, true)]
    [InlineData(SecurityAuditInitiator.VoiceCommand, RequestOrigin.ActivatedVoice, true, true)]
    [InlineData(SecurityAuditInitiator.ModelSuggestion, RequestOrigin.LocalUi, true, true)]
    [InlineData(SecurityAuditInitiator.ModelSuggestion, RequestOrigin.ActivatedVoice, true, true)]
    [InlineData(SecurityAuditInitiator.ModelSuggestion, RequestOrigin.ActivatedVoice, true, false)]
    [InlineData(SecurityAuditInitiator.TypedCommand, RequestOrigin.LocalUi, false, false)]
    [InlineData(SecurityAuditInitiator.ModelSuggestion, RequestOrigin.ActivatedVoice, false, false)]
    public async Task EveryLockRouteUsesTheExactScriptGrantAndExistingNativeBroker(
        SecurityAuditInitiator initiator, RequestOrigin origin, bool qualified, bool approve)
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
            var platform = new BundledNativePlatform();
            var worker = new BundledNativeWorker();
            var audit = new BundledNativeAudit();
            using var controller = new BundledFixedActionController(native.Main, sources, questions, lifecycle,
                storage.Store, new(storage.Tasks), storage.Authorization,
                qualified ? worker : new WindowsBundledScriptWorker(),
                new(new WindowsFixedSessionEffect(platform), audit, NullLogger<FixedSessionActionBroker>.Instance),
                renderer, () => owner, () => true, () => 1, NullLogger<BundledFixedActionController>.Instance);
            var opened = new TaskCompletionSource<QuestionWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var observe = Window.WindowOpenedEvent.AddClassHandler<QuestionWindow>((window, _) => opened.TrySetResult(window));
            try
            {
                using var original = HostActivity.BeginRoot(HostRequest.Create(origin),
                    HostActivityLayer.Desktop, HostOperation.Request);
                var running = native.Main.ExecuteAsync(native.Main.Commands.Single(command => command.Action == BuiltInAction.LockMachine),
                    initiator, native.Main.CallPolicyRevision);
                if (qualified)
                {
                    if (await Task.WhenAny(opened.Task, running) == running)
                    {
                        await running;
                        opened.Task.IsCompleted.Should().BeTrue(controller.Status);
                    }
                    var question = await opened.Task.WaitAsync(storage.Token);
                    platform.Requests.Should().BeEmpty();
                    worker.Invocations.Should().BeEmpty();
                    if (approve)
                    {
                        question.GetLogicalDescendants().OfType<RadioButton>().Single(item => item.Content is "Once").IsChecked = true;
                        question.FindControl<Button>("ReviewQuestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await BundledButtonEnabled(question.FindControl<Button>("SubmitAnswer")!).WaitAsync(storage.Token);
                        question.FindControl<Button>("SubmitAnswer")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    else { question.Close(); }
                }
                await running;
                platform.Requests.Should().HaveCount(qualified && approve ? 1 : 0);
                worker.Invocations.Should().HaveCount(qualified && approve ? 1 : 0);
                if (qualified && approve)
                {
                    worker.Invocations[0].Origin.Should().Be(origin);
                    audit.Events.Select(item => item.Outcome)
                        .Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Unknown);
                    audit.Events[1].ReasonCode.Should().Be("request-accepted-physical-state-unobserved");
                    controller.Status.Should().Contain("Physical lock or computer-power completion is not observed");
                }
                else { audit.Events.Should().BeEmpty(); }
                native.Main.IsModelActionApprovalPending.Should().BeFalse();
            }
            finally { owner.Close(); }
        });
    }
}
