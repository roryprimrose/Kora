using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Tools;
using Kora.NativeUxFixture;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class DesktopLifetimeControllerTests
{
    [Fact]
    public Task ExitWithProductionWorkspaceAccessCompletesAfterJoiningBothWorkOwners() =>
        HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(ScratchParent());
            await fixture.InitializeAsync();
            var ownership = new DesktopInstanceOwnershipBridge();
            ownership.BindCallbacks(_ => Task.CompletedTask, _ => Task.FromResult(true));
            using var access = new DesktopSessionWorkspaceAccess(ownership);
            access.Bind(fixture.Main);
            var control = access.ControlRevision;
            var dispatch = new NoLifecycleDispatch();
            var tasks = new HostTaskCoordinator(fixture.Tasks);
            var workspace = new SessionWorkspaceService(fixture.Interactions, tasks, access,
                NullLogger<SessionWorkspaceService>.Instance);
            await using var conversations = new SessionConversationService(fixture.Interactions, fixture.Interactions,
                workspace, access, dispatch, fixture.Time, NullLogger<SessionConversationService>.Instance);
            await using var queue = new SessionQueueService(fixture.Interactions, fixture.Interactions, tasks, access,
                dispatch, new(), NullLogger<SessionQueueService>.Instance, fixture.Time);
            using var questions = new NativeQuestionHost(fixture.Interactions, fixture.Time,
                NullLogger<NativeQuestionViewModel>.Instance);
            using var lifecycle = new ApplicationLifecycleController(fixture.Main, conversations, queue, questions,
                tasks, new(NullLogger<NativeDetailRenderer>.Instance),
                () => throw new InvalidOperationException("Idle Exit must not open a work-confirmation window."));
            fixture.Main.BindConversationShutdown(() => Task.FromResult(true), () => true);
            var shutdowns = 0;
            using var lifetime = new DesktopLifetimeController(fixture.Main, () =>
            {
                access.CanControl.Should().BeFalse("new input remains closed at the desktop shutdown effect");
                access.ControlRevision.Should().Be(control, "voluntary input closure does not revoke private ownership");
                shutdowns++;
            }, NullLogger<DesktopLifetimeController>.Instance);

            await fixture.Main.ExitAsync();

            shutdowns.Should().Be(1);
            dispatch.Calls.Should().Be(0);
            fixture.Voice.StartCalls.Should().Be(0);
            fixture.Voice.ReplyCalls.Should().Be(0);
        });

    [Fact]
    public Task TrayExitCompletesAfterReplacementConstructionFailureLeavesNoPresenceWindow() =>
        HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(ScratchParent());
            await fixture.InitializeAsync();
            var shutdowns = 0;
            using var lifetime = new DesktopLifetimeController(fixture.Main, () => shutdowns++,
                NullLogger<DesktopLifetimeController>.Instance);
            var surfaces = new List<Surface>();
            var presence = new PresenceWindowController((_, startup) =>
            {
                if (!startup) { throw new InvalidOperationException("fixture replacement construction failure"); }
                var surface = new Surface();
                surfaces.Add(surface);
                return surface;
            }, () => true);
            presence.Start(false);
            presence.Request(true);
            presence.Invoking(controller => controller.TryApply()).Should().Throw<InvalidOperationException>()
                .WithMessage("fixture replacement construction failure");
            surfaces.Should().ContainSingle().Which.Retired.Should().BeTrue();
            presence.IsAvailable.Should().BeFalse();
            shutdowns.Should().Be(0);

            using var tray = new SystemTrayController(fixture.Main, NullLogger<SystemTrayController>.Instance);
            var close = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Main.WindowActionRequested += (_, action) =>
            {
                if (action == WindowAction.Close) { close.TrySetResult(); }
            };
            var exit = TrayIcon.GetIcons(Avalonia.Application.Current!)!.Single().Menu!.Items
                .OfType<NativeMenuItem>().Single(item => string.Equals(item.Header, "Exit Kora", StringComparison.Ordinal));
            ((INativeMenuItemExporterEventsImplBridge)exit).RaiseClicked();
            await close.Task.WaitAsync(fixture.Token);
            shutdowns.Should().Be(1, "an admitted Exit must terminate the desktop even without a presence renderer");
            surfaces.Should().ContainSingle("Exit must not construct a fallback renderer");
            fixture.Voice.StartCalls.Should().Be(0);
            fixture.Voice.ReplyCalls.Should().Be(0);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task LifetimeShutdownStillRequiresReviewAndTheActualSubmittedWorkJoin(bool confirm) =>
        HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(ScratchParent());
            await fixture.InitializeAsync();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releasedReview = false;
            var joined = false;
            var shutdowns = 0;
            var closeObserved = false;
            var controllers = new List<IDisposable>();
            fixture.Main.BindSubmittedWorkShutdown((_, _, current) =>
                Task.FromResult<ApplicationLifecycleShutdown?>(confirm
                    ? new(current, async () =>
                    {
                        entered.SetResult();
                        await release.Task;
                        joined = true;
                    }, () => releasedReview = true)
                    : null));
            using var lifetime = new DesktopLifetimeController(fixture.Main, () =>
            {
                joined.Should().BeTrue("the lifetime binding cannot supply work-control authority or bypass physical joins");
                shutdowns++;
                foreach (var controller in controllers) { controller.Dispose(); }
            }, NullLogger<DesktopLifetimeController>.Instance);
            controllers.Add(lifetime);
            void ObserveInstallerClose(object? sender, WindowAction action)
            {
                if (action == WindowAction.Close) { closeObserved = true; }
            }
            fixture.Main.WindowActionRequested += ObserveInstallerClose;
            var exiting = fixture.Main.ExitAsync();
            try
            {
                if (confirm)
                {
                    await entered.Task.WaitAsync(fixture.Token);
                    exiting.IsCompleted.Should().BeFalse();
                    shutdowns.Should().Be(0);
                    closeObserved.Should().BeFalse();
                    releasedReview.Should().BeFalse();
                }
            }
            finally
            {
                release.TrySetResult();
                try { await exiting; }
                finally { fixture.Main.WindowActionRequested -= ObserveInstallerClose; }
            }
            shutdowns.Should().Be(confirm ? 1 : 0);
            closeObserved.Should().Be(confirm, "installer close observation must survive desktop lifetime disposal during shutdown");
            joined.Should().Be(confirm);
            releasedReview.Should().Be(confirm);
            fixture.Voice.StartCalls.Should().Be(0);
            fixture.Voice.ReplyCalls.Should().Be(0);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task StaleOrFailedSubmittedWorkJoinNeverShutsDownTheDesktop(bool failJoin) =>
        HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(ScratchParent());
            await fixture.InitializeAsync();
            var current = true;
            var shutdowns = 0;
            var released = false;
            fixture.Main.BindSubmittedWorkShutdown((_, _, _) =>
                Task.FromResult<ApplicationLifecycleShutdown?>(new(() => current, () =>
                {
                    if (failJoin) { throw new IOException("fixture physical join failure"); }
                    current = false;
                    return Task.CompletedTask;
                }, () => released = true)));
            using var lifetime = new DesktopLifetimeController(fixture.Main, () => shutdowns++,
                NullLogger<DesktopLifetimeController>.Instance);
            await fixture.Main.ExitAsync();
            shutdowns.Should().Be(0);
            released.Should().BeTrue();
            fixture.Main.ResponseTitle.Should().Be("Exit or restart was not confirmed.");
            fixture.Main.ResponseBody.Should().Contain("No application effect was performed");
        });

    [Fact]
    public Task PresentationAndDisposedBindingsCannotTerminateTheDesktop() =>
        HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(ScratchParent());
            await fixture.InitializeAsync();
            var shutdowns = 0;
            using var lifetime = new DesktopLifetimeController(fixture.Main, () => shutdowns++,
                NullLogger<DesktopLifetimeController>.Instance);
            fixture.Main.ShowApplication();
            await fixture.Main.HideApplicationAsync();
            shutdowns.Should().Be(0);
            lifetime.Dispose();
            lifetime.Dispose();
            var closeRequests = 0;
            fixture.Main.WindowActionRequested += (_, action) =>
            {
                if (action == WindowAction.Close) { closeRequests++; }
            };
            await fixture.Main.ExitAsync();
            closeRequests.Should().Be(1);
            shutdowns.Should().Be(0);
        });

    [Fact]
    public Task LifetimeEffectFailureIsReportedWithoutAFallbackOrDuplicateAttempt() =>
        HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(ScratchParent());
            await fixture.InitializeAsync();
            var attempts = 0;
            using var lifetime = new DesktopLifetimeController(fixture.Main, () =>
            {
                attempts++;
                throw new InvalidOperationException("fixture native lifetime failure");
            }, fixture.Loggers.CreateLogger<DesktopLifetimeController>());
            await fixture.Main.ExitAsync();
            attempts.Should().Be(1);
            fixture.Main.ResponseBody.Should().Contain("Desktop shutdown failed")
                .And.Contain("has not confirmed a clean exit");
            await fixture.Main.ExitAsync();
            attempts.Should().Be(1, "an uncertain native failure must not trigger an automatic retry");
        });

    private static string ScratchParent()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, ".git")) && !Directory.Exists(Path.Combine(root.FullName, ".git")))
        { root = root.Parent ?? throw new InvalidOperationException("The desktop lifecycle fixture requires its owning repository."); }
        var path = Path.Combine(root.FullName, ".scratch", "continuous-delivery",
            "b259b360-3c94-46f6-99c0-a55242f23d5d", "desktop-lifetime-fixtures");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class NoLifecycleDispatch : IModelTurnAdmission, IDeterministicVersionQueueAction
    {
        internal int Calls { get; private set; }
        public bool IsQuiescent => true;
        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
            ModelHandoffReview? review, CancellationToken token)
        {
            Calls++;
            throw new InvalidOperationException("Lifecycle validation cannot admit a model turn.");
        }

        public CapabilityReply Observe(SessionQueueReadProfile profile, CancellationToken token,
            SessionQueueCapabilityTarget? target = null)
        {
            Calls++;
            throw new InvalidOperationException("Lifecycle validation cannot dispatch a compatibility action.");
        }
    }

    private sealed class Surface : IPresenceWindow
    {
        internal bool Retired { get; private set; }
        public bool CanReplace => true;
        public bool IsReplacementReady => true;
        public bool PrepareForReplacement() => true;
        public void CancelReplacement() { }
        public PresenceWindowState SuspendForReplacement() =>
            new(new PixelPoint(100, 150), WindowState.Normal, true, true, null, 0);
        public void RestoreAfterReplacement(PresenceWindowState state) { }
        public void CloseForReplacement()
        {
            Retired = true;
        }
    }
}
