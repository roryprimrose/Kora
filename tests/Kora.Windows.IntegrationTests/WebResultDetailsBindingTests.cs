using System.Net;
using System.Text;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Application.Network;
using Kora.Core.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Network;
using Kora.Core.Presentation;
using Kora.NativeUxFixture;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class WebResultDetailsBindingTests
{
    [Fact]
    public async Task NativeWebSummaryControlsRequireExactLocalReviewAndNeverRefetch()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var transport = new Transport();
            using var fixture = new NativeUxFixtureSession(Path.GetFullPath("."));
            var model = new SummaryExecution();
            var workflow = new WebSummaryWorkflow(new(), model, fixture.Audit, TimeProvider.System,
                NullLogger<WebSummaryWorkflow>.Instance);
            await fixture.InitializeAsync(new(transport, fixture.Audit), workflow);
            await KeepResponseVisibleAsync(fixture);
            var window = new ResponseWindow(fixture.Main);
            try
            {
                window.Show();
                await RetrieveAsync(fixture);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var source = fixture.Main.WebResultDetailsReference;
                var prepare = window.GetVisualDescendants().OfType<Button>().Single(button =>
                    string.Equals(button.Content as string, "Review local summary of this exact web result", StringComparison.Ordinal));
                prepare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                model.Calls.Should().Be(0);
                fixture.Main.WebSummaryStatus.Should().Contain("Only host session/task receipts are retained");
                window.IsVisible.Should().BeTrue();
                fixture.Main.WebSummaryConfirmations.Should().ContainSingle();
                var confirm = window.GetVisualDescendants().OfType<Button>().Single(button =>
                    string.Equals(button.Content as string, "Use this exact web result with the local model", StringComparison.Ordinal));
                confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                fixture.Main.WebSummaryText.Should().Be("Admitted synthetic local answer.");
                fixture.Main.WebSummaryStatus.Should().Contain("Not a live page");
                fixture.Main.WebResultDetailsReference.Should().Be(source);
                model.Calls.Should().Be(1);
                transport.SendCalls.Should().Be(1);
                transport.ResolveCalls.Should().Be(1);
                window.Close();
                fixture.Main.WebSummaryText.Should().BeEmpty();
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task ActualResponseButtonOpensExactNativeSnapshotWithoutAnyFurtherNetworkOrRetargetingOldControl()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var transport = new Transport();
            using var fixture = new NativeUxFixtureSession(Path.GetFullPath("."));
            await fixture.InitializeAsync(new(transport, fixture.Audit));
            await KeepResponseVisibleAsync(fixture);
            var views = new List<View>();
            var clipboard = new Clipboard();
            using var details = new DetailWindowController(fixture.Documents, () => fixture.Main.CanRevealPrivatePresentation,
                new(NullLogger<NativeDetailRenderer>.Instance), clipboard, (state, result, _) =>
                {
                    result.IsFallback.Should().BeFalse();
                    result.SemanticText.Should().Be(state.Content!.Source);
                    var view = new View(state);
                    views.Add(view);
                    return view;
                }, NullLogger<DetailWindowController>.Instance);
            details.BindWebResultSource(fixture.Main.ResolveWebResultDetails);
            fixture.Main.WebResultDetailsChanged += (_, _) => details.RetireUnavailableWebResults();
            var window = new ResponseWindow(fixture.Main, details);
            try
            {
                window.Show();
                await RetrieveAsync(fixture);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var originalButton = window.GetVisualDescendants().OfType<Button>()
                    .Single(button => string.Equals(button.Content as string, "Open exact web-result details", StringComparison.Ordinal));
                var original = (DetailContentReference)originalButton.Tag!;
                originalButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                originalButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var view = views.Single();
                view.Activations.Should().Be(2);
                view.State.Content!.WebResult!.Text.Should().Be("exact <script>inert</script> https://example.com/image");
                view.State.Search("inert").Should().BeTrue();
                view.State.SetSource(true);
                view.State.ActiveText.Should().Contain("<script>inert</script>");
                await details.CopyAsync(original, view.State.Generation, false);
                clipboard.Writes.Should().BeEmpty();
                await details.CopyAsync(original, view.State.Generation, true);
                clipboard.Writes.Should().ContainSingle().Which.Should().Be(view.State.Content!.Source);
                transport.SendCalls.Should().Be(1);
                transport.ResolveCalls.Should().Be(1);
                fixture.Main.IsModelQuestionPending.Should().BeFalse();
                fixture.Main.IsModelActionApprovalPending.Should().BeFalse();

                await RetrieveAsync(fixture);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                view.Cleared.Should().BeTrue();
                originalButton.Tag.Should().BeNull("removing the old item clears its binding, never retargets it to the latest result");
                originalButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                fixture.Main.WebResultDetailStatus.Should().Contain("retired");
                views.Should().ContainSingle();
                var next = window.GetVisualDescendants().OfType<Button>()
                    .Single(button => string.Equals(button.Content as string, "Open exact web-result details", StringComparison.Ordinal));
                next.Should().NotBeSameAs(originalButton);
                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                views.Should().HaveCount(2);
                transport.SendCalls.Should().Be(2);
                transport.ResolveCalls.Should().Be(2);
                window.Close();
                fixture.Main.HasWebResultDetails.Should().BeFalse();
                views[1].State.ActiveText.Should().BeEmpty();
            }
            finally { window.Close(); }
        });
    }

    private static async Task KeepResponseVisibleAsync(NativeUxFixtureSession fixture)
    {
        // Source-control tests must not race the unrelated real-time inactivity timer on a shared host.
        using var request = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        (await fixture.Main.SetResponseAlwaysVisibleAsync(true)).Should().BeTrue();
    }

    private static async Task RetrieveAsync(NativeUxFixtureSession fixture)
    {
        using var request = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        await fixture.Main.ExecuteWebPageCommandAsync(new(new("https://example.com/")), SecurityAuditInitiator.TypedCommand, fixture.Token);
    }

    private sealed class Transport : IWebPageTransport
    {
        public int SendCalls { get; private set; }
        public int ResolveCalls { get; private set; }
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            ResolveCalls++;
            return Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("93.184.216.34")]);
        }
        public Task<WebPageResponse> SendAsync(Uri address, IPAddress endpoint, CancellationToken cancellationToken)
        {
            SendCalls++;
            var bytes = Encoding.UTF8.GetBytes("exact <script>inert</script> https://example.com/image");
            return Task.FromResult(new WebPageResponse(200, address, "text/plain", "utf-8", bytes.Length,
                null, [], new MemoryStream(bytes)));
        }
    }

    private sealed class View(DetailViewerState state) : IDetailView
    {
        public DetailViewerState State { get; } = state;
        public bool Cleared { get; private set; }
        public int Activations { get; private set; }
        public event EventHandler? Closed;
        public void ShowOwned(Window? owner) => owner.Should().NotBeNull();
        public void Activate() => Activations++;
        public void ClearAndClose()
        {
            if (Cleared) { return; }
            Cleared = true;
            State.Close();
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class Clipboard : IDetailClipboard
    {
        public List<string> Writes { get; } = [];
        public Task WriteAsync(IDetailView view, string source, string? htmlFragment)
        {
            Writes.Add(source);
            return Task.CompletedTask;
        }

    }

    private sealed class SummaryExecution : IWebSummaryModelExecution
    {
        internal int Calls { get; private set; }
        public Task<ModelTurnResult> Run(WebSummaryReview review, Func<bool> current, CancellationToken token)
        {
            current().Should().BeTrue();
            Calls++;
            return Task.FromResult(new ModelTurnResult(ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome,
                Response: new("Admitted synthetic local answer.", null)));
        }
    }
}
