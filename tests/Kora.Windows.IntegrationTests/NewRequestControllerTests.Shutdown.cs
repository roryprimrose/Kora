using Avalonia.Controls;
using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Storage;
using Kora.NativeUxFixture;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class NewRequestControllerTests
{
    [Theory]
    [InlineData("discard")]
    [InlineData("cancel")]
    [InlineData("reopen")]
    [InlineData("failure")]
    public async Task ProductionShutdownGuardProtectsNewRequestAndEveryTopicAcrossHostQuiescence(string outcome)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            var access = new ShutdownAccess(f.Access, f.Main);
            var conversation = new SessionConversationService(f.Interactions, f.Interactions, f.Sessions,
                access, new NoAdmission(), TimeProvider.System, NullLogger<SessionConversationService>.Instance);
            using var sessions = new SessionsWindowController(f.Main, f.Sessions, f.Evidence, access,
                NullLogger<SessionsViewModel>.Instance, conversation: conversation);
            using var requests = new NewRequestWindowController(f.Main, f.Sessions, access,
                () => new(f.Sessions, conversation, access, NullLogger<NewRequestComposer>.Instance), sessions);
            sessions.Bind();
            requests.Bind();
            ConversationShutdownGuard.Bind(f.Main, requests, sessions);
            SessionsWindow? workspace = null;
            NewRequestWindow? request = null;
            using var sessionOpened = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) => workspace = view);
            using var requestOpened = Window.WindowOpenedEvent.AddClassHandler<NewRequestWindow>((view, _) => request = view);
            sessions.Open();
            var state = (SessionsViewModel)workspace!.DataContext!;
            await state.RefreshAsync();
            await state.SelectAsync(state.Sessions.First(row => row.Authority.IsActive));
            state.ComposerBody = "topic input retained while unselected";
            await state.SelectAsync(null);
            state.ComposerBody.Should().BeEmpty();
            state.HasUnsentMessages.Should().BeTrue();
            requests.Open();
            ((NewRequestViewModel)request!.DataContext!).Composer.Body = "new request input";
            request.DecideDraft = () => Task.FromResult(ComposerDraftDecision.Discard);
            var sessionDecisions = 0;
            state.DecideComposerDraft = () =>
            {
                sessionDecisions++;
                if (outcome is "failure") { throw new IOException("Synthetic session review failure."); }
                if (outcome is "reopen")
                {
                    requests.Open();
                    ((NewRequestViewModel)request!.DataContext!).Composer.Body = "newer independent input";
                }
                return Task.FromResult(outcome is "cancel" ? ComposerDraftDecision.Cancel : ComposerDraftDecision.Discard);
            };
            var closed = false;
            f.Main.WindowActionRequested += (_, action) => closed |= action == WindowAction.Close;
            await f.Main.ExitAsync();
            sessionDecisions.Should().Be(1);
            closed.Should().Be(outcome is "discard");
            if (outcome is "cancel" or "failure") { state.HasUnsentMessages.Should().BeTrue(); }
            if (outcome is "reopen")
            {
                request!.IsVisible.Should().BeTrue();
                ((NewRequestViewModel)request.DataContext!).Composer.Body.Should().Be("newer independent input");
            }
            f.Main.BindConversationShutdown(null);
        });
    }

    private sealed class ShutdownAccess(ISessionWorkspaceAccess inner, MainViewModel main) : ISessionWorkspaceAccess
    {
        public bool CanInspect => inner.CanInspect && main.CanRevealPrivatePresentation;
        public bool CanControl => CanInspect && inner.CanControl;
        public long ControlRevision => inner.ControlRevision;
    }
}
