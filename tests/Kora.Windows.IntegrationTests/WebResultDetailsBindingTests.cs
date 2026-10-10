using System.Net;
using System.Text;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Presentation;
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
    public async Task ActualResponseButtonOpensExactNativeSnapshotWithoutAnyFurtherNetworkOrRetargetingOldControl()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var transport = new Transport();
            using var fixture = new NativeUxFixtureSession(Path.GetFullPath("."));
            await fixture.InitializeAsync(new(transport, fixture.Audit));
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
        public Task WritePlainTextAsync(IDetailView view, string source)
        {
            Writes.Add(source);
            return Task.CompletedTask;
        }
    }
}
