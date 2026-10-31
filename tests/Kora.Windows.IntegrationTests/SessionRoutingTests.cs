using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Hosting.SessionRouting;
using Kora.Core.Conversation.SessionRouting;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.NativeUxFixture;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed partial class SessionRoutingTests
{
    [Theory]
    [InlineData(false, RequestOrigin.LocalUi)]
    [InlineData(true, RequestOrigin.LocalUi)]
    [InlineData(false, RequestOrigin.ActivatedVoice)]
    [InlineData(true, RequestOrigin.ActivatedVoice)]
    public async Task BothModesCompleteCorrectedDestinationWithoutMovingOtherInputOrReadingPosition(bool expanded, RequestOrigin origin)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize(expanded);
            var prior = f.Model.SelectedSessionRecord;
            f.Model.ComposerBody = "independent unsent input";
            var rows = f.Model.Sessions.ToArray();
            var draft = await f.Native.Interactions.ReadConversationDraftAsync(f.First.Authority.SessionId, f.Native.Token);
            var opening = new TaskCompletionSource<SessionRoutingWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((view, _) => opening.TrySetResult(view));
            var operation = f.Routing.RouteAsync("  original request\r\nnot a command  ", origin, static () => true);
            var window = await AwaitOpening(operation, opening.Task);
            var choices = Find<ComboBox>(window, "RoutingDestinations");
            choices.ItemCount.Should().Be(2, "one clear exact candidate and New, never the selected UI conversation by implication");
            await Choose(window);
            Find<TextBlock>(window, "RoutingAcknowledgment").Text.Should().StartWith("Destination: Alpha.");
            f.Admission.Calls.Should().Be(0);
            Click(window, "RoutingCorrect");
            Find<Button>(window, "RoutingSubmit").IsEnabled.Should().BeFalse();
            choices.ItemCount.Should().Be(rows.Length + 1);
            choices.SelectedIndex = Array.FindIndex(choices.Items.Cast<object>().ToArray(),
                value => value.ToString()!.EndsWith(": Beta", StringComparison.Ordinal));
            await Choose(window);
            Find<TextBlock>(window, "RoutingAcknowledgment").Text.Should().StartWith("Destination: Beta.");
            Click(window, "RoutingSubmit");
            var receipt = (await operation)!;
            receipt.Target.Authority.SessionId.Should().Be(f.Second.Authority.SessionId);
            receipt.Submission.Message.Request.Origin.Should().Be(origin);
            receipt.Submission.Message.Body.Should().Be("  original request\r\nnot a command  ");
            receipt.Submission.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
            f.Admission.Calls.Should().Be(1);
            f.Model.SelectedSessionRecord.Should().Be(prior);
            f.Model.ComposerBody.Should().Be("independent unsent input");
            f.Model.Sessions.Should().Equal(rows);
            f.Window!.IsExpanded.Should().Be(expanded);
            (await f.Native.Interactions.ReadConversationDraftAsync(f.First.Authority.SessionId, f.Native.Token)).Should().Be(draft);
            window.Content.Should().BeNull();
            window.IsVisible.Should().BeFalse();
        });
    }

    [Theory]
    [InlineData(SessionRouteMatch.Ambiguous)]
    [InlineData(SessionRouteMatch.NoMatch)]
    [InlineData(SessionRouteMatch.Unavailable)]
    public async Task NativeAmbiguityAndUnavailableFallbackCompleteFreshNewOnlyAfterAcknowledgment(SessionRouteMatch match)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture { Match = match };
            await f.Initialize(expanded: true);
            var initial = await f.Native.Interactions.ReadMetadataPageAsync(null, 50, f.Native.Token);
            var opening = new TaskCompletionSource<SessionRoutingWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((view, _) => opening.TrySetResult(view));
            var operation = f.Routing.RouteAsync("fresh original", RequestOrigin.LocalUi, static () => true);
            var window = await AwaitOpening(operation, opening.Task);
            var choices = Find<ComboBox>(window, "RoutingDestinations");
            choices.ItemCount.Should().Be(match == SessionRouteMatch.Ambiguous ? 3 : 1);
            (await f.Native.Interactions.ReadMetadataPageAsync(null, 50, f.Native.Token)).Records.Should().Equal(initial.Records);
            choices.SelectedIndex = choices.ItemCount - 1;
            await Choose(window);
            Find<TextBlock>(window, "RoutingAcknowledgment").Text.Should().Contain("New conversation");
            Click(window, "RoutingSubmit");
            var result = (await operation)!;
            result.Target.Metadata!.Name.Value.Should().Be("New conversation");
            initial.Records.Should().NotContain(entry => entry.Authority.SessionId == result.Target.Authority.SessionId);
            (await f.Native.Interactions.ReadMetadataPageAsync(null, 50, f.Native.Token)).Records.Should().HaveCount(initial.Records.Length + 1);
            f.Admission.Calls.Should().Be(1);
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("rename")]
    [InlineData("done")]
    [InlineData("dispose")]
    [InlineData("source")]
    public async Task NativeWithdrawalNeverCreatesFallbackOrSubmitsALateChoice(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize(expanded: false);
            var live = true;
            var opening = new TaskCompletionSource<SessionRoutingWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((view, _) => opening.TrySetResult(view));
            var operation = f.Routing.RouteAsync("held original", RequestOrigin.LocalUi, () => live);
            var window = await AwaitOpening(operation, opening.Task);
            await Choose(window);
            var late = Find<Button>(window, "RoutingSubmit");
            if (change is "cancel") { Click(window, "RoutingCancel"); }
            if (change is "rename")
            {
                await f.Native.Sessions.RenameAsync(f.First.Authority.SessionId, f.First.Authority.Generation,
                    f.First.Metadata!.Revision.Value, new("Renamed"), RequestOrigin.LocalUi, f.Native.Token);
            }
            if (change is "done")
            {
                await f.Native.Sessions.ChangeLifecycleAsync(f.First.Authority.SessionId, f.First.Authority.Generation,
                    false, RequestOrigin.LocalUi, f.Native.Token);
            }
            if (change is "dispose") { f.Routing.Dispose(); }
            if (change is "source")
            {
                live = false;
                late.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Click(window, "RoutingCancel");
            }
            Dispatcher.UIThread.RunJobs();
            late.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            (await operation).Should().BeNull();
            f.Admission.Calls.Should().Be(0);
            (await f.Native.Interactions.ReadMetadataPageAsync(null, 50, f.Native.Token)).Records.Should().HaveCount(5);
            window.Content.Should().BeNull();
        });
    }

    private static T Find<T>(SessionRoutingWindow window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => string.Equals(control.Name, name, StringComparison.Ordinal));

    [Fact]
    public async Task ShutdownWithdrawsReviewAndJoinsAnAlreadyOwnedCanonicalSubmission()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize(expanded: true);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Admission.Wait = async () => { started.SetResult(); await release.Task; };
            var opening = new TaskCompletionSource<SessionRoutingWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((view, _) => opening.TrySetResult(view));
            var operation = f.Routing.RouteAsync("original", RequestOrigin.LocalUi, static () => true);
            var window = await AwaitOpening(operation, opening.Task);
            await Choose(window);
            Click(window, "RoutingSubmit");
            await Task.WhenAny(started.Task, operation);
            if (operation.IsCompleted) { await operation; throw new InvalidOperationException("Submission returned before model admission."); }
            await started.Task;
            var previous = f.Routing.CaptureShutdownEligibility();
            previous().Should().BeFalse();
            var shutdown = f.Routing.PrepareShutdown();
            shutdown.IsCompleted.Should().BeFalse("cancellation is not resource quiescence");
            release.SetResult();
            (await shutdown).Should().BeTrue();
            f.Routing.CaptureShutdownEligibility()().Should().BeTrue();
            await this.Invoking(async _ => await operation).Should().ThrowAsync<Exception>();
            f.Admission.Calls.Should().Be(1);
        });
    }

    private static void Click(SessionRoutingWindow window, string name) =>
        Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task<SessionRoutingWindow> AwaitOpening(Task<SessionRoutingReceipt?> operation, Task<SessionRoutingWindow> opening)
    {
        await Task.WhenAny(operation, opening);
        if (operation.IsCompleted) { await operation; throw new InvalidOperationException("Routing returned without a native review."); }
        return await opening;
    }

    private static async Task Choose(SessionRoutingWindow window)
    {
        Click(window, "RoutingChoose");
        await window.ChoiceOperation;
    }

    private sealed class Fixture : IDisposable
    {
        internal NativeUxFixtureSession Native { get; } = new(Path.GetFullPath("."), silentCaption: true);
        internal Admission Admission { get; } = new();
        internal SessionRouteMatch Match { get; set; } = SessionRouteMatch.Clear;
        internal SessionRoutingController Routing { get; private set; } = null!;
        internal SessionWorkspaceEntry First { get; private set; } = null!;
        internal SessionWorkspaceEntry Second { get; private set; } = null!;
        internal SessionsViewModel Model => (SessionsViewModel)Window!.SessionView.DataContext!;
        internal ResponseWindow? Window { get; private set; }
        private SessionsWindowController? sessions;
        private ResponseWindowController? responses;
        private IDisposable? adviceGestures;
        internal bool AutoAllowAdvice { get; set; } = true;
        internal int Matches { get; set; }

        internal async Task Initialize(bool expanded)
        {
            adviceGestures = Avalonia.Controls.Window.WindowOpenedEvent.AddClassHandler<SessionRoutingAdviceWindow>((window, _) =>
            {
                if (AutoAllowAdvice) { Dispatcher.UIThread.Post(() => ClickAdvice(window, "RoutingAdviceAllow")); }
            });
            await Native.InitializeAsync();
            First = await Native.Sessions.CreateAsync(new("Alpha"), RequestOrigin.LocalUi, Native.Token);
            Second = await Native.Sessions.CreateAsync(new("Beta"), RequestOrigin.LocalUi, Native.Token);
            var conversation = new SessionConversationService(Native.Interactions, Native.Interactions, Native.Sessions,
                Native.Access, Admission, TimeProvider.System, NullLogger<SessionConversationService>.Instance);
            responses = new(Native.Main, NullLogger<ResponseWindowController>.Instance);
            sessions = new(Native.Main, Native.Sessions, Native.Evidence, Native.Access, NullLogger<SessionsViewModel>.Instance, conversation: conversation);
            sessions.CreateInteraction = (model, owner) => Window = responses.PrepareSessions(model, owner, sessions.Open, static () => { });
            sessions.Bind();
            await sessions.ShowInteractionAsync(expanded: true, static () => true);
            await Model.SelectAsync(Model.Sessions.Single(row => row.Authority.SessionId == First.Authority.SessionId));
            if (!expanded) { Window!.ShowSessions(expanded: false); }
            Routing = new(Native.Main, Native.Sessions, conversation, Native.Access, TimeProvider.System, new Matcher(this));
            Routing.Bind();
        }

        public void Dispose()
        {
            Routing?.Dispose();
            sessions?.Dispose();
            responses?.Dispose();
            adviceGestures?.Dispose();
            Native.Dispose();
        }

    }

    private static void ClickAdvice(SessionRoutingAdviceWindow window, string name) =>
        window.GetVisualDescendants().OfType<Button>().Single(item => string.Equals(item.Name, name, StringComparison.Ordinal))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private sealed class Matcher(Fixture fixture) : ISessionRelatednessMatcher
    {
        public Task<SessionRouteProposal> MatchAsync(string request, SessionRouteSnapshot snapshot, Func<bool> admitted, CancellationToken token)
        {
            fixture.Matches++;
            return Task.FromResult(new SessionRouteProposal(snapshot.Revision, fixture.Match, fixture.Match switch
            {
                SessionRouteMatch.Clear => [snapshot.Descriptors.Single(item => item.Entry.Authority.SessionId == fixture.First.Authority.SessionId).Candidate],
                SessionRouteMatch.Ambiguous => [.. snapshot.Descriptors.Where(item =>
                    item.Entry.Authority.SessionId == fixture.First.Authority.SessionId
                    || item.Entry.Authority.SessionId == fixture.Second.Authority.SessionId).Select(item => item.Candidate)],
                _ => [],
            }));
        }
    }

    private sealed class Admission : IModelTurnAdmission
    {
        internal int Calls { get; private set; }
        internal Func<Task>? Wait { get; set; }
        public bool IsQuiescent => true;
        public async Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context, ModelHandoffReview? review, CancellationToken token)
        {
            Calls++;
            if (Wait is { } wait) { await wait(); }
            return new(new(ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel));
        }
    }
}
