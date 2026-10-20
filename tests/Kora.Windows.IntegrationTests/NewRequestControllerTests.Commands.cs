using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Application;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class NewRequestControllerTests
{
    private static async Task OpenNewRequestCommand(MainViewModel main)
    {
        main.CommandText = "open new request";
        await main.RunTypedCommand.ExecuteAsync();
    }

    [WindowsFact]
    public async Task ExactCommandSharesSingletonWithoutChangingDirtyReviewedSavedOrHeldState()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            var admission = new NewRequestUnavailableAdmission();
            var conversation = new SessionConversationService(f.Interactions, f.Interactions, f.Sessions, f.Access,
                admission, TimeProvider.System, NullLogger<SessionConversationService>.Instance);
            using var sessions = new SessionsWindowController(f.Main, f.Sessions, f.Evidence, f.Access,
                NullLogger<SessionsViewModel>.Instance, conversation: conversation);
            var creates = 0;
            using var controller = new NewRequestWindowController(f.Main, f.Sessions, f.Access, () =>
            {
                creates++;
                return new(f.Sessions, conversation, f.Access, NullLogger<NewRequestComposer>.Instance);
            }, sessions, new AvaloniaUiDispatcher());
            controller.Bind();
            NewRequestWindow? window = null;
            var opens = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<NewRequestWindow>((view, _) => { window = view; opens++; });
            var emptyBefore = await f.Interactions.ReadMetadataPageAsync(null, 50, f.Token);
            using (var voiceNavigation = Kora.Core.Diagnostics.HostActivity.BeginRoot(
                HostRequest.Create(RequestOrigin.ActivatedVoice), Kora.Core.Diagnostics.HostActivityLayer.Desktop,
                Kora.Core.Diagnostics.HostOperation.Presentation))
            {
                controller.Open();
            }
            await OpenNewRequestCommand(f.Main);
            (await f.Interactions.ReadMetadataPageAsync(null, 50, f.Token)).Records.Should().Equal(emptyBefore.Records);
            var state = ((NewRequestViewModel)window!.DataContext!).Composer;
            state.Creation.Should().BeNull();
            state.Submission.Should().BeNull();
            state.Body.Should().BeEmpty();
            state.Body = "  exact isolated memory\r\nnot a command body  ";
            await OpenNewRequestCommand(f.Main);
            controller.Open();
            state.Dirty.Should().BeTrue();
            state.Body.Should().Be("  exact isolated memory\r\nnot a command body  ");
            await state.Create();
            state.Creation!.Request.Origin.Should().Be(RequestOrigin.LocalUi,
                "the subsequent genuine native gesture owns creation, not the navigation request");
            var exact = state.Target!.Authority.SessionId;
            var before = await f.Interactions.ReadMetadataPageAsync(null, 50, f.Token);
            var retention = await f.Interactions.ReadRetentionAsync(exact, f.Token);
            var draft = await f.Interactions.ReadConversationDraftAsync(exact, f.Token);
            state.Review();
            var reviewed = state.ReviewedBody;
            var status = state.Status;
            await OpenNewRequestCommand(f.Main);
            state.ReviewedBody.Should().Be(reviewed);
            state.Status.Should().Be(status);
            state.CanSubmit.Should().BeTrue();
            await state.ReviewSave();
            state.CanSave.Should().BeTrue();
            status = state.Status;
            await OpenNewRequestCommand(f.Main);
            state.CanSave.Should().BeTrue();
            state.Status.Should().Be(status);
            (await f.Interactions.ReadMetadataPageAsync(null, 50, f.Token)).Records.Should().Equal(before.Records);
            (await f.Interactions.ReadRetentionAsync(exact, f.Token)).Should().Be(retention);
            (await f.Interactions.ReadConversationDraftAsync(exact, f.Token)).Should().Be(draft);
            admission.Calls.Should().Be(0);
            // A separately requested native submission failure prepares held state; opening must not retry it.
            state.Review();
            await state.Submit();
            state.CanReview.Should().BeFalse();
            status = state.Status;
            var body = state.Body;
            var submission = state.Submission;
            var calls = admission.Calls;
            await OpenNewRequestCommand(f.Main);
            state.Status.Should().Be(status);
            state.Body.Should().Be(body);
            state.Submission.Should().Be(submission);
            state.CanReview.Should().BeFalse();
            admission.Calls.Should().Be(calls);
            ((NewRequestViewModel)window.DataContext!).Composer.Should().BeSameAs(state);
            creates.Should().Be(1);
            opens.Should().Be(1);
        });
    }

    [WindowsFact]
    public async Task NavigationDuringComposerBusyNotificationDoesNotCancelOrReplaceItsOriginalAttempt()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            var conversation = new SessionConversationService(f.Interactions, f.Interactions, f.Sessions, f.Access,
                new NoAdmission(), TimeProvider.System, NullLogger<SessionConversationService>.Instance);
            using var sessions = new SessionsWindowController(f.Main, f.Sessions, f.Evidence, f.Access,
                NullLogger<SessionsViewModel>.Instance, conversation: conversation);
            using var controller = new NewRequestWindowController(f.Main, f.Sessions, f.Access,
                () => new(f.Sessions, conversation, f.Access, NullLogger<NewRequestComposer>.Instance), sessions,
                new CommandNavigationDispatcher());
            controller.Bind();
            NewRequestWindow? window = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<NewRequestWindow>((view, _) => window = view);
            await OpenNewRequestCommand(f.Main);
            var state = ((NewRequestViewModel)window!.DataContext!).Composer;
            state.Body = "busy memory";
            var observed = false;
            state.PropertyChanged += (_, args) =>
            {
                if (observed || !string.Equals(args.PropertyName, nameof(state.Busy), StringComparison.Ordinal) || !state.Busy) { return; }
                observed = true;
                var attempt = state.Creation;
                OpenNewRequestCommand(f.Main).IsCompletedSuccessfully.Should().BeTrue();
                state.Busy.Should().BeTrue();
                state.Creation.Should().BeSameAs(attempt);
                state.Body.Should().Be("busy memory");
            };
            await state.Create();
            observed.Should().BeTrue();
            state.Target.Should().NotBeNull();
        });
    }

    [Theory]
    [InlineData("control")]
    [InlineData("privacy")]
    [InlineData("close")]
    [InlineData("source")]
    [InlineData("dispose")]
    public async Task QueuedNativeOpeningCannotReopenOrFocusAfterItsExactLifetimeCloses(string change)
    {
        if (!OperatingSystem.IsWindows()) { return; }
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            var conversation = new SessionConversationService(f.Interactions, f.Interactions, f.Sessions, f.Access,
                new NoAdmission(), TimeProvider.System, NullLogger<SessionConversationService>.Instance);
            using var sessions = new SessionsWindowController(f.Main, f.Sessions, f.Evidence, f.Access,
                NullLogger<SessionsViewModel>.Instance, conversation: conversation);
            var dispatcher = new CommandNavigationDispatcher();
            using var controller = new NewRequestWindowController(f.Main, f.Sessions, f.Access,
                () => new(f.Sessions, conversation, f.Access, NullLogger<NewRequestComposer>.Instance), sessions, dispatcher);
            controller.Bind();
            NewRequestWindow? window = null;
            var opens = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<NewRequestWindow>((view, _) => { window = view; opens++; });
            await OpenNewRequestCommand(f.Main);
            var state = ((NewRequestViewModel)window!.DataContext!).Composer;
            state.Body = "dirty memory must not be replaced";
            if (change is "source") { await state.Create(); }
            dispatcher.Defer = true;
            var pending = OpenNewRequestCommand(f.Main);
            dispatcher.Pending.Should().NotBeNull();
            switch (change)
            {
                case "control": f.Access.SetOpen(true); break;
                case "privacy":
                    f.Access.SetOpen(false);
                    await f.Main.PrivacyClosureTask;
                    break;
                case "close": window.ClosePrivate(); break;
                case "source": controller.RevokeSession(state.Creation!.Request.SessionId); break;
                case "dispose": controller.Dispose(); break;
            }
            await dispatcher.Dispatch();
            await pending;
            opens.Should().Be(1);
            if (change is "control")
            {
                state.Body.Should().Be("dirty memory must not be replaced");
                window.IsVisible.Should().BeTrue();
            }
            else
            {
                state.Body.Should().BeEmpty();
                window.IsVisible.Should().BeFalse();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnerLossDuringFactoryOrNativeShowNeverLeavesLatePrivateContentVisible(bool duringShow)
    {
        if (!OperatingSystem.IsWindows()) { return; }
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            var conversation = new SessionConversationService(f.Interactions, f.Interactions, f.Sessions, f.Access,
                new NoAdmission(), TimeProvider.System, NullLogger<SessionConversationService>.Instance);
            using var sessions = new SessionsWindowController(f.Main, f.Sessions, f.Evidence, f.Access,
                NullLogger<SessionsViewModel>.Instance, conversation: conversation);
            NewRequestComposer? state = null;
            using var controller = new NewRequestWindowController(f.Main, f.Sessions, f.Access, () =>
            {
                state = new(f.Sessions, conversation, f.Access, NullLogger<NewRequestComposer>.Instance);
                state.Body = "private memory";
                if (!duringShow) { f.Access.SetOpen(false); }
                return state;
            }, sessions, new CommandNavigationDispatcher());
            controller.Bind();
            NewRequestWindow? window = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<NewRequestWindow>((view, _) =>
            {
                window = view;
                f.Access.SetOpen(false);
            });
            await OpenNewRequestCommand(f.Main);
            state!.Body.Should().BeEmpty();
            state.CanCreate.Should().BeFalse();
            (window?.IsVisible ?? false).Should().BeFalse();
            await f.Main.PrivacyClosureTask;
        });
    }

    private sealed class CommandNavigationDispatcher : IUiDispatcher
    {
        private TaskCompletionSource? completion;
        public bool Defer { get; set; }
        public Func<Task>? Pending { get; private set; }
        public Task InvokeAsync(Func<Task> action)
        {
            if (!Defer) { return action(); }
            Pending = action;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return completion.Task;
        }
        public async Task Dispatch()
        {
            await Pending!();
            Pending = null;
            completion!.SetResult();
        }
        public void Post(Action action) => throw new InvalidOperationException("Navigation never posts unobserved work.");
    }

    private sealed class NewRequestUnavailableAdmission : IModelTurnAdmission
    {
        public int Calls { get; private set; }
        public bool IsQuiescent => true;
        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
            ModelHandoffReview? review, CancellationToken token)
        {
            Calls++;
            throw new IOException("Synthetic separately requested native submission failure.");
        }
    }
}
