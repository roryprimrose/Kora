using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.NativeUxFixture;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class NewRequestControllerTests
{
    [Fact]
    public async Task HostedFreshRequestCreatesAndSubmitsOnlyItsNewTargetWithoutChangingNamedComposer()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetFullPath("."), silentCaption: true);
            await f.InitializeAsync();
            var conversation = new SessionConversationService(f.Interactions, f.Interactions, f.Sessions, f.Access,
                new UnavailableTurnAdmission(), TimeProvider.System, NullLogger<SessionConversationService>.Instance);
            using var responses = new ResponseWindowController(f.Main, NullLogger<ResponseWindowController>.Instance);
            using var sessions = new SessionsWindowController(f.Main, f.Sessions, f.Evidence, f.Access,
                NullLogger<SessionsViewModel>.Instance, conversation: conversation);
            sessions.CreateInteraction = (model, presentation) => responses.PrepareSessions(model, presentation,
                sessions.Open, static () => { });
            sessions.Bind();
            await sessions.ShowInteractionAsync(true, static () => true);
            var shell = responses.PrepareWorkbenchOwner();
            var named = (SessionsViewModel)shell.SessionView.DataContext!;
            await named.SelectAsync(named.Sessions.First(row => row.Authority.IsActive));
            named.ComposerBody = "independent named topic input";
            var selected = named.SelectedSessionRecord!;
            using var requests = new NewRequestWindowController(f.Main, f.Sessions, f.Access,
                () => new(f.Sessions, conversation, f.Access, NullLogger<NewRequestComposer>.Instance), sessions,
                new AvaloniaUiDispatcher());
            requests.BindWorkbench(responses.PrepareWorkbenchOwner,
                view => responses.ShowHostedView(WorkbenchActivity.NewRequest, view));
            requests.Bind();
            var windows = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<NewRequestWindow>((_, _) => windows++);
            requests.Open();
            var view = requests.View!;
            var composer = ((NewRequestViewModel)view.DataContext!).Composer;
            composer.Body = "isolated fresh request";
            view.FindControl<Button>("CreateTarget")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await view.Operation;
            composer.Target!.Authority.SessionId.Should().NotBe(selected.Authority.SessionId);
            view.FindControl<Button>("ReviewNewRequest")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            composer.CanSubmit.Should().BeTrue(composer.Status);
            view.FindControl<Button>("SubmitNewRequest")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await view.Operation;
            composer.Submission.Should().NotBeNull(composer.Status);
            composer.Status.Should().Contain("Exact USER message retained").And.Contain("Unavailable");
            var message = await f.Interactions.ReadConversationMessageAsync(composer.Target.Authority.SessionId,
                composer.Submission!.RequestId, f.Token);
            message!.Body.Should().Be("isolated fresh request");
            named.ComposerBody.Should().Be("independent named topic input");
            named.SelectedSessionRecord.Should().BeSameAs(selected);
            windows.Should().Be(0);
            composer.Body.Should().BeEmpty();
            shell.ActiveActivity.Should().Be(WorkbenchActivity.NewRequest);
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("discard")]
    [InlineData("hide")]
    public async Task HostedNewRequestKeepsMemoryUnlessItsExactDiscardIsExplicitlyConfirmed(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetFullPath("."), silentCaption: true);
            await f.InitializeAsync();
            var conversation = new SessionConversationService(f.Interactions, f.Interactions, f.Sessions, f.Access,
                new NoAdmission(), TimeProvider.System, NullLogger<SessionConversationService>.Instance);
            using var responses = new ResponseWindowController(f.Main, NullLogger<ResponseWindowController>.Instance);
            using var sessions = new SessionsWindowController(f.Main, f.Sessions, f.Evidence, f.Access,
                NullLogger<SessionsViewModel>.Instance, conversation: conversation);
            using var requests = new NewRequestWindowController(f.Main, f.Sessions, f.Access,
                () => new(f.Sessions, conversation, f.Access, NullLogger<NewRequestComposer>.Instance), sessions,
                new AvaloniaUiDispatcher());
            requests.BindWorkbench(responses.PrepareWorkbenchOwner,
                view => responses.ShowHostedView(WorkbenchActivity.NewRequest, view));
            requests.Open();
            var shell = responses.PrepareWorkbenchOwner();
            var view = requests.View!;
            var composer = ((NewRequestViewModel)view.DataContext!).Composer;
            composer.Body = "unsent independent request";
            if (string.Equals(boundary, "hide", StringComparison.Ordinal))
            {
                shell.Hide();
                requests.Open();
                composer.Body.Should().Be("unsent independent request");
                requests.View.Should().BeSameAs(view);
                composer.Creation.Should().BeNull();
                return;
            }
            view.FindControl<Button>("CloseNewRequest")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            shell.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var body = shell.FindControl<ContentControl>("NativeReviewContent")!;
            var decision = body.GetVisualDescendants().OfType<Button>().Single(button =>
                string.Equals(button.Name, string.Equals(boundary, "discard", StringComparison.Ordinal)
                    ? "DiscardBeforeClose" : "CancelClose", StringComparison.Ordinal));
            decision.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await view.Operation;
            composer.Body.Should().Be(string.Equals(boundary, "discard", StringComparison.Ordinal) ? string.Empty : "unsent independent request");
            requests.View.Should().Be(string.Equals(boundary, "discard", StringComparison.Ordinal) ? null : view);
            shell.IsVisible.Should().BeTrue("closing a composer does not close its owning workbench");
            composer.Creation.Should().BeNull();
        });
    }

    private sealed class UnavailableTurnAdmission : IModelTurnAdmission
    {
        public bool IsQuiescent => true;
        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
            ModelHandoffReview? review, CancellationToken token) =>
            Task.FromResult(new ModelTurnAdmission(new(ModelTurnOutcome.Unavailable, ModelTurnReason.GenerationNotEnabled)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HostedOriginalReuseRequiresSeparateConfirmationAndNeverCreatesOrSubmitsATarget(bool confirm)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetFullPath("."), silentCaption: true);
            await f.InitializeAsync();
            var (source, original) = await RetainOriginal(f);
            var conversation = new SessionConversationService(f.Interactions, f.Interactions, f.Sessions, f.Access,
                new NoAdmission(), TimeProvider.System, NullLogger<SessionConversationService>.Instance);
            using var responses = new ResponseWindowController(f.Main, NullLogger<ResponseWindowController>.Instance);
            using var sessions = new SessionsWindowController(f.Main, f.Sessions, f.Evidence, f.Access,
                NullLogger<SessionsViewModel>.Instance, conversation: conversation);
            using var requests = new NewRequestWindowController(f.Main, f.Sessions, f.Access,
                () => new(f.Sessions, conversation, f.Access, NullLogger<NewRequestComposer>.Instance), sessions,
                new AvaloniaUiDispatcher());
            requests.BindWorkbench(responses.PrepareWorkbenchOwner,
                view => responses.ShowHostedView(WorkbenchActivity.NewRequest, view));
            requests.Bind();
            var shell = responses.PrepareWorkbenchOwner();
            shell.Show();
            shell.Activate();
            var before = (await f.Interactions.ReadMetadataPageAsync(null, 50, f.Token)).Records.Length;
            var opens = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<OriginalMessageReuseWindow>((_, _) => opens++);
            var presented = new TaskCompletionSource<OriginalMessageReuseView>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnLayout(object? sender, EventArgs args)
            {
                var actual = shell.FindControl<ContentControl>("NativeReviewContent")!
                    .GetVisualDescendants().OfType<OriginalMessageReuseView>().FirstOrDefault();
                if (actual is not null) { presented.TrySetResult(actual); }
            }
            shell.LayoutUpdated += OnLayout;
            using var root = HostActivity.BeginRoot(original.Request, HostActivityLayer.Desktop, HostOperation.Request);
            var operation = requests.ReviewOriginalReuseAsync(source, original, shell, static () => true, f.Token);
            OriginalMessageReuseView reuseView;
            try
            {
                var next = await Task.WhenAny(presented.Task, operation).WaitAsync(TimeSpan.FromSeconds(5), f.Token);
                if (ReferenceEquals(next, operation))
                { throw new InvalidOperationException("Reuse operation ended before native review publication: " + await operation); }
                reuseView = await presented.Task;
            }
            finally { shell.LayoutUpdated -= OnLayout; }
            requests.View.Should().BeNull("displaying a review cannot open or seed New request");
            (await f.Interactions.ReadMetadataPageAsync(null, 50, f.Token)).Records.Should().HaveCount(before);
            reuseView.GetVisualDescendants().OfType<Button>().Single(button =>
                string.Equals(button.Name, confirm ? "ConfirmOriginalMessageReuse" : "CancelOriginalMessageReuse", StringComparison.Ordinal))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var outcome = await operation.WaitAsync(TimeSpan.FromSeconds(5), f.Token);
            if (confirm)
            {
                outcome.Should().Contain("memory only");
                var composer = ((NewRequestViewModel)requests.View!.DataContext!).Composer;
                composer.Body.Should().Be(original.Body);
                composer.Target.Should().BeNull();
                composer.Submission.Should().BeNull();
                shell.ActiveActivity.Should().Be(WorkbenchActivity.NewRequest);
            }
            else
            {
                outcome.Should().Contain("cancelled");
                requests.View.Should().BeNull();
            }
            (await f.Interactions.ReadMetadataPageAsync(null, 50, f.Token)).Records.Should().HaveCount(before);
            opens.Should().Be(0);
        });
    }
}
