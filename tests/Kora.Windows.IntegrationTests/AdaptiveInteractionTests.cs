using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Visuals;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.NativeUxFixture;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class AdaptiveInteractionTests
{
    [Fact]
    public async Task ExpandedWorkspaceHasNoOrdinaryInactivityTimerAndHideKeepsUnsentInput()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.Run("select the first session");
            f.Model.ComposerBody = "kept while hidden";
            var window = f.Window!;
            // The legacy response timer has no injected clock; inspect its scheduling state, not wall time.
            var inactivity = (PresentationInactivityTimeout)typeof(ResponseWindow).GetField("responseInactivity",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
            inactivity.IsScheduled.Should().BeFalse();
            await f.Run("what power action is pending");
            inactivity.IsScheduled.Should().BeFalse();
            window.IsSessionListPresented.Should().BeTrue();
            window.FindControl<Button>("HideInteraction")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.IsVisible.Should().BeFalse();
            f.Model.ComposerBody.Should().Be("kept while hidden");
            await f.Run("show sessions");
            f.Window.Should().BeSameAs(window);
            f.Model.ComposerBody.Should().Be("kept while hidden");
        });
    }

    [Fact]
    public async Task DisposalReentrantWithNativeOpeningRetiresTheOriginalSessionOwner()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            var retired = false;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<ResponseWindow>((_, _) =>
            {
                if (retired) { return; }
                retired = true;
                f.Sessions.Dispose();
            });
            await f.Run("show sessions");
            f.Window!.IsVisible.Should().BeFalse();
            f.Model.CanRead.Should().BeFalse();
            f.Model.Sessions.Should().BeEmpty();
            f.Model.ComposerBody.Should().BeEmpty();
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Fact]
    public async Task TrayVoiceAndButtonsReuseResponseWindowAndPreservePerSessionBuffers()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            var opens = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<ResponseWindow>((_, _) => opens++);
            await f.Run("what power action is pending");
            await f.Run("show me the current sessions");
            var window = f.Window!;
            var model = f.Model;
            opens.Should().Be(1, "ordinary feedback and expanded sessions use the same native window");
            window.IsExpanded.Should().BeTrue();
            window.CanResize.Should().BeTrue();
            model.SelectedSessionRecord.Should().BeNull();
            model.ConversationMessages.Should().BeEmpty("opening admits metadata only, not conversation bodies");
            model.SessionFilter.Should().Be(SessionListFilter.Active);
            model.Sessions.Should().HaveCountGreaterThanOrEqualTo(3);
            var first = model.Sessions[0];
            var last = model.Sessions[^1];
            await f.Run("select the top session");
            model.SelectedSessionRecord.Should().Be(first);
            model.Status.Should().StartWith("Selected ");
            model.ConversationMessages.Should().BeEmpty();
            model.ComposerBody = "first conversation unsent";
            await f.Run("select the last session");
            model.SelectedSessionRecord.Should().Be(last);
            model.ComposerBody.Should().BeEmpty();
            model.ComposerBody = "last conversation unsent";
            await f.Run("select the third session");
            model.SelectedSessionRecord.Should().Be(model.Sessions[2]);
            await f.Run("select the first session");
            model.ComposerBody.Should().Be("first conversation unsent");
            var authority = model.SelectedSessionRecord!.Authority;

            window.FindControl<Button>("CollapseSessions")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.IsExpanded.Should().BeFalse();
            window.CanResize.Should().BeFalse();
            model.ComposerBody.Should().Be("first conversation unsent");
            f.Native.Main.ShowSessions();
            await f.Sessions.InteractionOperation;
            opens.Should().Be(1);
            window.IsExpanded.Should().BeTrue();
            window.SessionView.FindControl<TextBox>("SessionComposer")!.Text.Should().Be(model.ComposerBody);
            var rows = model.Sessions.ToArray();
            await model.RefreshWorkAsync();
            model.Sessions.Should().Equal(rows, "background work refresh must not reorder the presented page");
            (await f.Native.Interactions.ReadMetadataAsync(authority.SessionId, f.Native.Token)).Authority.Should().Be(authority);

            window.FindControl<Button>("ShowCurrentInteraction")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.IsSessionListPresented.Should().BeFalse();
            model.ComposerBody.Should().Be("first conversation unsent");
            await f.Run("show sessions");
            window.IsSessionListPresented.Should().BeTrue();
            opens.Should().Be(1);
        });
    }

    [Theory]
    [InlineData("refresh")]
    [InlineData("filter")]
    [InlineData("collapse")]
    [InlineData("collapse-return")]
    [InlineData("hide-return")]
    [InlineData("owner")]
    [InlineData("privacy")]
    [InlineData("dispose")]
    public async Task CapturedOrdinalCannotSelectAReplacementAfterItsSourceChanges(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            var model = f.Model;
            var captured = f.Sessions.CaptureSessionSelection(3, static () => true);
            switch (boundary)
            {
                case "refresh": await model.RefreshAsync(); break;
                case "filter": model.SessionFilter = SessionListFilter.Done; break;
                case "collapse": await f.Run("collapse conversation"); break;
                case "collapse-return":
                    await f.Run("collapse conversation");
                    await f.Run("show sessions");
                    break;
                case "hide-return":
                    f.Window!.FindControl<Button>("HideInteraction")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await f.Run("show sessions");
                    break;
                case "owner": f.Native.Access.SetOpen(false); f.Native.Access.SetOpen(true); break;
                case "privacy":
                    f.Native.Main.CloseForObservedPrivacyEvent("Synthetic adaptive privacy closure", hidePresentation: true);
                    await f.Native.Main.PrivacyClosureTask;
                    break;
                case "dispose": f.Sessions.Dispose(); break;
            }
            await captured();
            model.SelectedSessionRecord.Should().BeNull();
            model.ConversationMessages.Should().BeEmpty();
            model.ComposerBody.Should().BeEmpty();
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Fact]
    public async Task OutOfRangeOrUnpresentedOrdinalsNeverCreateOrSelectTargetsAndDoneReadsNeverResume()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("select the third session");
            f.Native.Main.ResponseBody.Should().Contain("Show sessions first");
            await f.Run("show sessions");
            await f.Run("select the twenty-fifth session");
            f.Model.SelectedSessionRecord.Should().BeNull();
            f.Native.Main.ResponseBody.Should().Contain("no replacement");
            f.Model.SessionFilter = SessionListFilter.Done;
            await f.Model.RefreshAsync();
            var target = f.Model.Sessions.Single(row => row.Authority.SessionId == f.Done!.Authority.SessionId);
            var rows = f.Model.Sessions.ToArray();
            await f.Sessions.CaptureSessionSelection(Array.IndexOf(rows, target) + 1, static () => true)();
            f.Model.SelectedSessionRecord.Should().Be(target);
            f.Model.CanCompose.Should().BeFalse();
            f.Window!.SessionView.FindControl<Button>("ReadActivity")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await f.Window.SessionView.Operation;
            f.Model.HistoryRecords.Should().NotBeEmpty();
            (await f.Native.Interactions.ReadMetadataAsync(target.Authority.SessionId, f.Native.Token))
                .Authority.Should().Be(target.Authority);
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Fact]
    public async Task PrivacyRevokesBothModesAndClearsPrivateInput()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.Run("select the first session");
            var original = f.Model;
            original.ComposerBody = "private memory";
            var window = f.Window!;
            f.Native.Access.SetOpen(false);
            f.Native.Main.CloseForObservedPrivacyEvent("Synthetic adaptive privacy closure", hidePresentation: true);
            await f.Native.Main.PrivacyClosureTask;
            window.IsVisible.Should().BeFalse();
            original.ComposerBody.Should().BeEmpty();
            original.CanRead.Should().BeFalse();
            original.ConversationMessages.Should().BeEmpty();
        });
    }

    private sealed class Fixture : IDisposable
    {
        internal NativeUxFixtureSession Native { get; } = new(Path.GetFullPath("."), silentCaption: true);
        internal NoAdmission Admission { get; } = new();
        internal SessionsWindowController Sessions { get; private set; } = null!;
        private ResponseWindowController? responses;
        private AllWorkWindowController? all;
        private WorkspaceSurfaceCommandController? commands;
        internal ResponseWindow? Window { get; private set; }
        internal SessionsViewModel Model => (SessionsViewModel)Window!.SessionView.DataContext!;
        internal SessionWorkspaceEntry? Done { get; private set; }

        internal async Task Initialize()
        {
            await Native.InitializeAsync();
            foreach (var name in new[] { "First topic", "Second topic", "Third topic" })
            { await Native.Sessions.CreateAsync(new(name), RequestOrigin.LocalUi, Native.Token); }
            Done = await Native.Sessions.CreateAsync(new("Completed topic"), RequestOrigin.LocalUi, Native.Token);
            await Native.Sessions.ChangeLifecycleAsync(Done.Authority.SessionId, Done.Authority.Generation,
                false, RequestOrigin.LocalUi, Native.Token);
            var conversation = new SessionConversationService(Native.Interactions, Native.Interactions,
                Native.Sessions, Native.Access, Admission, TimeProvider.System, NullLogger<SessionConversationService>.Instance);
            responses = new(Native.Main, NullLogger<ResponseWindowController>.Instance);
            Sessions = new(Native.Main, Native.Sessions, Native.Evidence, Native.Access,
                NullLogger<SessionsViewModel>.Instance, conversation: conversation);
            Sessions.CreateInteraction = (model, owner) =>
                Window = responses.PrepareSessions(model, owner, Sessions.Open, static () => { });
            Sessions.Bind();
            all = new(Native.Main, Native.Sessions, Native.Access, NullLogger<AllWorkViewModel>.Instance, Sessions);
            commands = new(Native.Main, Native.Access, all, Sessions);
            commands.Bind();
        }

        internal async Task Run(string phrase)
        {
            Native.Main.CommandText = phrase;
            await Native.Main.RunTypedCommand.ExecuteAsync();
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            commands?.Dispose();
            all?.Dispose();
            Sessions?.Dispose();
            responses?.Dispose();
            Native.Dispose();
        }
    }

    private sealed class NoAdmission : IModelTurnAdmission
    {
        internal int Calls { get; private set; }
        public bool IsQuiescent => true;
        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
            ModelHandoffReview? review, CancellationToken token)
        {
            Calls++;
            throw new InvalidOperationException("Navigation must never admit model execution.");
        }
    }
}
