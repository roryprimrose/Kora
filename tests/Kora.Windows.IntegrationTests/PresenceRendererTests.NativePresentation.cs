using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Media;

using AwesomeAssertions;

using Kora.Controls;

namespace Kora.Windows.IntegrationTests;

public sealed partial class PresenceRendererTests
{
    [Fact]
    public async Task StandardPresenterUsesVisibleAvaloniaPixelsWithoutDrawingOnTheHost()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var clock = new RecordingScheduler();
            using var source = new PresenceControl(clock) { IsPresentationEligible = true };
            var window = Show(source);
            var target = new BitmapTarget();
            var failures = new List<Exception>();
            using var presenter = new AvaloniaPresencePresenter(window, source, () => true,
                failures.Add, _ => target);
            clock.Advance(TimeSpan.FromSeconds(1));
            failures.Should().BeEmpty();
            target.Presentations.Should().BeGreaterThan(0);
            target.Pixels.Should().Contain(value => value != 0);
            target.Pixels.Where((_, index) => index % 4 == 3).Should().Contain(value => value == 0);
            presenter.CanInteract.Should().BeTrue();
            var hostDrawing = new DrawingGroup();
            using (var context = hostDrawing.Open()) { source.Render(context); }
            hostDrawing.Children.Should().BeEmpty("the clipped host must not draw a second particle cloud");
            var frame = new DrawingGroup();
            using (var context = frame.Open()) { source.DrawFrame(context); }
            frame.Children.Should().HaveCount(150);

            source.Arrange(new Rect(0, 0, 240, 300));
            presenter.Refresh();
            target.Size.Should().Be((240, 300));
            window.Hide();
            presenter.CanInteract.Should().BeFalse();
            target.Hidden.Should().BeTrue();
            window.Close();
        });
    }

    [Fact]
    public async Task StandardPresenterRechecksAdmissionAfterPixelCopyAndSuppressesRetiredFrames()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var clock = new RecordingScheduler();
            using var source = new PresenceControl(clock) { IsPresentationEligible = true };
            var window = Show(source);
            var admitted = true;
            var target = new BitmapTarget { AfterCopy = () => admitted = false };
            var failures = new List<Exception>();
            using var presenter = new AvaloniaPresencePresenter(window, source, () => admitted,
                failures.Add, _ => target);
            clock.Advance(TimeSpan.FromSeconds(1));
            failures.Should().BeEmpty();
            target.Copies.Should().Be(1);
            target.Presentations.Should().Be(0);
            target.Hidden.Should().BeTrue();
            presenter.CanInteract.Should().BeFalse();
            target.AfterCopy = null;
            admitted = true;
            presenter.Refresh();
            target.Presentations.Should().Be(1);
            source.IsPresentationEligible = false;
            presenter.CanInteract.Should().BeFalse();
            target.Hidden.Should().BeTrue();
            presenter.Dispose();
            var copies = target.Copies;
            source.IsPresentationEligible = true;
            clock.Advance(TimeSpan.FromSeconds(1));
            clock.InvokeRetired();
            target.Copies.Should().Be(copies);
            target.ReleaseOrder.Should().Equal("buffer", "window");
            window.Close();
        });
    }

    [Fact]
    public async Task StandardPresenterReportsFailureAndDoesNotRetryOrSubstituteDrawing()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var clock = new RecordingScheduler();
            using var source = new PresenceControl(clock) { IsPresentationEligible = true };
            var window = Show(source);
            var failure = new IOException("synthetic pixel-copy failure");
            var target = new BitmapTarget { CopyFailure = failure };
            var failures = new List<Exception>();
            using var presenter = new AvaloniaPresencePresenter(window, source, () => true,
                failures.Add, _ => target);
            clock.Advance(TimeSpan.FromSeconds(1));
            failures.Should().ContainSingle().Which.Should().BeSameAs(failure);
            target.Presentations.Should().Be(0);
            target.Copies.Should().Be(1);
            target.Hidden.Should().BeTrue();
            var hostDrawing = new DrawingGroup();
            using (var context = hostDrawing.Open()) { source.Render(context); }
            hostDrawing.Children.Should().BeEmpty();
            presenter.Refresh();
            target.Copies.Should().Be(1);
            window.Close();
        });
    }

    [Fact]
    public async Task StandardPresenterCannotPublishAFrameWhoseHostWasHiddenDuringCopy()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var clock = new RecordingScheduler();
            using var source = new PresenceControl(clock) { IsPresentationEligible = true };
            var window = Show(source);
            var target = new BitmapTarget { AfterCopy = window.Hide };
            var failures = new List<Exception>();
            using var presenter = new AvaloniaPresencePresenter(window, source, () => true,
                failures.Add, _ => target);
            clock.Advance(TimeSpan.FromSeconds(1));
            failures.Should().BeEmpty();
            target.Copies.Should().Be(1);
            target.Presentations.Should().Be(0);
            target.Hidden.Should().BeTrue();
            presenter.PresentedOpacity.Should().Be(0);
            window.Close();
        });
    }

    private sealed class BitmapTarget : AvaloniaPresencePresenter.IBitmapTarget
    {
        public nint Handle => 123;
        public byte[] Pixels { get; private set; } = [];
        public (int Width, int Height) Size { get; private set; }
        public int Copies { get; private set; }
        public int Presentations { get; private set; }
        public bool Hidden { get; private set; } = true;
        public Action? AfterCopy { get; set; }
        public Exception? CopyFailure { get; set; }
        public List<string> ReleaseOrder { get; } = [];
        public void Move(int x, int y) { }
        public void CopyFrame(int width, int height, Action<nint, int, int> copyPixels)
        {
            Copies++;
            if (CopyFailure is not null) { throw CopyFailure; }
            Size = (width, height);
            Pixels = new byte[checked(width * height * 4)];
            var buffer = Marshal.AllocHGlobal(Pixels.Length);
            try
            {
                copyPixels(buffer, Pixels.Length, width * 4);
                Marshal.Copy(buffer, Pixels, 0, Pixels.Length);
            }
            finally { Marshal.FreeHGlobal(buffer); }
            AfterCopy?.Invoke();
        }
        public void Present() { Presentations++; Hidden = false; }
        public void Hide() => Hidden = true;
        public void ReleaseDrawingResources() => ReleaseOrder.Add("buffer");
        public void Dispose() => ReleaseOrder.Add("window");
    }
}
