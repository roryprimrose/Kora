using System.Net;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Network.ExternalBrowser;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Network;
using Kora.Core.Network.ExternalBrowser;
using Kora.NativeUxFixture;
using Kora.Windows.Network.ExternalBrowser;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class ExternalBrowserTests
{
    [Theory]
    [InlineData(false, 14)]
    [InlineData(true, 24)]
    public Task LongDestinationCanBeReviewedAndConfirmedOrCancelledWithinTheNativeWindow(bool confirmOpening, int fontSize) =>
        HeadlessSession.RunAsync(async () =>
        {
            var transport = new Transport();
            using var fixture = new NativeUxFixtureSession(Path.GetFullPath("."));
            await fixture.InitializeAsync(new(transport, fixture.Audit));
            var launcher = new Launcher();
            fixture.Main.BindExternalBrowserWorkflow(new(launcher, new Network(), fixture.Audit, TimeProvider.System));
            using (var request = Root()) { await fixture.Main.SetResponseAlwaysVisibleAsync(true); }
            var response = new ResponseWindow(fixture.Main);
            response.Show();
            try
            {
                var exactAddress = "https://example.com/exact?q=" + new string('a', 3500) + "reviewed-end";
                using (var request = Root())
                {
                    await fixture.Main.ExecuteWebPageCommandAsync(new(new(exactAddress)),
                        SecurityAuditInitiator.TypedCommand, fixture.Token);
                }
                Dispatcher.UIThread.RunJobs();
                response.UpdateLayout();
                Click(Button(response, "Open in external browser"));
                var review = response.OwnedWindows.OfType<ExternalBrowserReviewWindow>().Single();
                review.Width = 420;
                review.Height = 500;
                review.FontSize = fontSize;
                review.UpdateLayout();
                var address = review.GetVisualDescendants().OfType<TextBox>().Single();
                var confirm = Button(review, "Open this exact destination once");
                var cancel = Button(review, "Cancel");
                address.FontSize = confirm.FontSize = cancel.FontSize = fontSize;
                foreach (var text in review.GetVisualDescendants().OfType<TextBlock>()) { text.FontSize = fontSize; }
                Dispatcher.UIThread.RunJobs();
                review.UpdateLayout();
                review.ClientSize.Height.Should().BeLessThanOrEqualTo(500);
                address.Text.Should().Be(exactAddress);
                address.Bounds.Height.Should().BeGreaterThan(60);
                foreach (var control in new Control[] { address, confirm, cancel })
                {
                    var bounds = control.TranslatePoint(default, review)!.Value;
                    bounds.X.Should().BeGreaterThanOrEqualTo(0);
                    bounds.Y.Should().BeGreaterThanOrEqualTo(0);
                    (bounds.X + control.Bounds.Width).Should().BeLessThanOrEqualTo(review.ClientSize.Width);
                    (bounds.Y + control.Bounds.Height).Should().BeLessThanOrEqualTo(review.ClientSize.Height);
                }
                var viewport = address.GetVisualDescendants().OfType<ScrollViewer>().Single();
                viewport.Extent.Height.Should().BeGreaterThan(viewport.Viewport.Height);
                address.Focus().Should().BeTrue();
                review.KeyPress(Key.End, RawInputModifiers.Control, PhysicalKey.End, null);
                review.KeyRelease(Key.End, RawInputModifiers.Control, PhysicalKey.End, null);
                review.UpdateLayout();
                address.CaretIndex.Should().Be(exactAddress.Length);
                viewport.Offset.Y.Should().BeGreaterThan(0, "the complete exact destination must be reachable by keyboard");
                address.Text.Should().Be(exactAddress);
                var privacy = review.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => string.Equals(text.Text, ExternalBrowserReview.Disclosure, StringComparison.Ordinal));
                var privacyViewport = privacy.GetVisualAncestors().OfType<ScrollViewer>().First();
                privacyViewport.Bounds.Height.Should().BeLessThanOrEqualTo(140);
                privacyViewport.Focus().Should().BeTrue();
                for (var page = 0; page <= Math.Ceiling(privacyViewport.Extent.Height / privacyViewport.Viewport.Height); page++)
                {
                    review.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
                    review.KeyRelease(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
                }
                review.UpdateLayout();
                privacyViewport.Offset.Y.Should().BeApproximately(
                    Math.Max(0, privacyViewport.Extent.Height - privacyViewport.Viewport.Height), 1);
                privacy.Text.Should().Be(ExternalBrowserReview.Disclosure);
                launcher.Calls.Should().Be(0);
                var action = confirmOpening ? confirm : cancel;
                action.Focus().Should().BeTrue();
                review.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                response.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Dispatcher.UIThread.RunJobs();
                review.IsVisible.Should().BeFalse();
                address.Text.Should().BeEmpty();
                Click(confirm);
                launcher.Calls.Should().Be(confirmOpening ? 1 : 0);
                launcher.Address.Should().Be(confirmOpening ? exactAddress : null);
                transport.SendCalls.Should().Be(1, "review and launch must not fetch another page");
            }
            finally { response.Close(); }
        });

    [Theory]
    [InlineData(true, ExternalBrowserLaunchOutcome.Accepted)]
    [InlineData(false, ExternalBrowserLaunchOutcome.Failed)]
    public async Task WindowsAdapterPassesOnlyTheExactUrlAndReportsOsAcceptance(bool accepted, ExternalBrowserLaunchOutcome expected)
    {
        var calls = new List<Uri>();
        var adapter = new WindowsExternalBrowserLauncher(address =>
        {
            calls.Add(address);
            return Task.FromResult(accepted);
        });
        var destination = ExternalBrowserDestination.Validate("https://example.com/path?q=exact%20target")!;
        (await adapter.LaunchAsync(destination, static () => true, TestContext.Current.CancellationToken)).Should().Be(expected);
        calls.Should().ContainSingle().Which.AbsoluteUri.Should().Be(destination.ExactAddress);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WindowsAdapterNeverInvokesNativeLaunchAfterDenialOrPreCancellation(bool cancelled)
    {
        var calls = 0;
        var adapter = new WindowsExternalBrowserLauncher(_ => { calls++; return Task.FromResult(true); });
        using var cancellation = new CancellationTokenSource();
        if (cancelled) { await cancellation.CancelAsync(); }
        (await adapter.LaunchAsync(ExternalBrowserDestination.Validate("https://example.com/")!,
            static () => false, cancellation.Token)).Should().Be(
                cancelled ? ExternalBrowserLaunchOutcome.Cancelled : ExternalBrowserLaunchOutcome.Denied);
        calls.Should().Be(0);
    }

    [Fact]
    public async Task WindowsFailureIsUnknownAndNeverRetried()
    {
        var calls = 0;
        var adapter = new WindowsExternalBrowserLauncher(_ =>
        {
            calls++;
            throw new InvalidOperationException("Synthetic native uncertainty.");
        });
        (await adapter.LaunchAsync(ExternalBrowserDestination.Validate("https://example.com/")!,
            static () => true, TestContext.Current.CancellationToken)).Should().Be(ExternalBrowserLaunchOutcome.Unknown);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task RealNativeSourceControlRequiresSeparateReviewCancelAndOneShotConfirmation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var transport = new Transport();
            using var fixture = new NativeUxFixtureSession(Path.GetFullPath("."));
            await fixture.InitializeAsync(new(transport, fixture.Audit));
            var launcher = new Launcher();
            fixture.Main.BindExternalBrowserWorkflow(new(launcher, new Network(), fixture.Audit, TimeProvider.System));
            using (var request = Root())
            { (await fixture.Main.SetResponseAlwaysVisibleAsync(true)).Should().BeTrue(); }
            var response = new ResponseWindow(fixture.Main);
            try
            {
                response.Show();
                await Retrieve(fixture);
                response.UpdateLayout();
                var open = Button(response, "Open in external browser");
                Click(open);
                var review = response.OwnedWindows.OfType<ExternalBrowserReviewWindow>().Single();
                review.GetVisualDescendants().OfType<TextBox>().Single().Text.Should().Be("https://example.com/exact?q=reviewed");
                review.GetVisualDescendants().OfType<TextBlock>().Should().Contain(block =>
                    block.Text != null && block.Text.Contains("cookies", StringComparison.Ordinal)
                    && block.Text.Contains("Kora cannot stop or close", StringComparison.Ordinal));
                launcher.Calls.Should().Be(0);
                Click(Button(review, "Cancel"));
                launcher.Calls.Should().Be(0);
                review.IsVisible.Should().BeFalse();
                Click(open);
                var next = response.OwnedWindows.OfType<ExternalBrowserReviewWindow>().Single(window => window.IsVisible);
                var confirm = Button(next, "Open this exact destination once");
                Click(confirm);
                Click(confirm);
                Dispatcher.UIThread.RunJobs();
                launcher.Calls.Should().Be(1);
                launcher.Address.Should().Be("https://example.com/exact?q=reviewed");
                fixture.Main.WebResultDetailStatus.Should().Contain("Windows accepted").And.Contain("cannot stop or close");
                transport.SendCalls.Should().Be(1);
                fixture.Main.WebResultDetailsReference.Should().NotBeNull();
            }
            finally { response.Close(); }
        });
    }

    [Fact]
    public async Task NativeSourceReplacementAndPrivacyClosureRetireExactReviewAndOldControls()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var transport = new Transport();
            using var fixture = new NativeUxFixtureSession(Path.GetFullPath("."));
            await fixture.InitializeAsync(new(transport, fixture.Audit));
            var launcher = new Launcher();
            fixture.Main.BindExternalBrowserWorkflow(new(launcher, new Network(), fixture.Audit, TimeProvider.System));
            using (var request = Root()) { await fixture.Main.SetResponseAlwaysVisibleAsync(true); }
            var response = new ResponseWindow(fixture.Main);
            try
            {
                response.Show();
                await Retrieve(fixture);
                response.UpdateLayout();
                var oldOpen = Button(response, "Open in external browser");
                Click(oldOpen);
                var oldReview = response.OwnedWindows.OfType<ExternalBrowserReviewWindow>().Single();
                var oldConfirm = Button(oldReview, "Open this exact destination once");
                await Retrieve(fixture);
                Dispatcher.UIThread.RunJobs();
                response.UpdateLayout();
                oldReview.IsVisible.Should().BeFalse();
                oldReview.GetVisualDescendants().OfType<TextBox>().Single().Text.Should().BeEmpty();
                Click(oldConfirm);
                oldOpen.Tag.Should().BeNull();
                Click(oldOpen);
                fixture.Main.WebResultDetailStatus.Should().Contain("retired");
                Click(Button(response, "Open in external browser"));
                var privacyReview = response.OwnedWindows.OfType<ExternalBrowserReviewWindow>().Single(window => window.IsVisible);
                var privacyConfirm = Button(privacyReview, "Open this exact destination once");
                await fixture.Main.HideApplicationAsync();
                Dispatcher.UIThread.RunJobs();
                privacyReview.IsVisible.Should().BeFalse();
                Click(privacyConfirm);
                launcher.Calls.Should().Be(0);
                transport.SendCalls.Should().Be(2);
            }
            finally { response.Close(); }
        });
    }

    private static Button Button(Window window, string label) => window.GetVisualDescendants().OfType<Button>()
        .Single(button => string.Equals(button.Content as string, label, StringComparison.Ordinal));
    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }
    private static HostActivity Root() => HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
        HostActivityLayer.Application, HostOperation.Request);
    private static async Task Retrieve(NativeUxFixtureSession fixture)
    {
        using var request = Root();
        await fixture.Main.ExecuteWebPageCommandAsync(new(new("https://example.com/exact?q=reviewed")),
            SecurityAuditInitiator.TypedCommand, fixture.Token);
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class Launcher : IExternalBrowserLauncher
    {
        public int Calls { get; private set; }
        public string? Address { get; private set; }
        public Task<ExternalBrowserLaunchOutcome> LaunchAsync(ExternalBrowserDestination destination, Func<bool> admitted, CancellationToken cancellationToken)
        {
            admitted().Should().BeTrue();
            Calls++;
            Address = destination.ExactAddress;
            return Task.FromResult(ExternalBrowserLaunchOutcome.Accepted);
        }
    }

    private sealed class Network : IExternalBrowserNetworkPolicy
    {
        public ExternalBrowserNetworkAdmission Capture() => new(true, static () => true);
    }

    private sealed class Transport : IWebPageTransport
    {
        public int SendCalls { get; private set; }
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("93.184.216.34")]);
        public Task<WebPageResponse> SendAsync(Uri address, IPAddress endpoint, CancellationToken cancellationToken)
        {
            SendCalls++;
            var content = Encoding.UTF8.GetBytes("Untrusted content cannot open https://elsewhere.example/ or javascript:evil.");
            return Task.FromResult(new WebPageResponse(200, address, "text/plain", "utf-8", content.Length,
                null, [], new MemoryStream(content)));
        }
    }
}
