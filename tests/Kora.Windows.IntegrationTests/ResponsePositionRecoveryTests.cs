using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Kora.NativeUxFixture;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class ResponsePositionRecoveryTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task ReopeningSavedResponseKeepsOriginalControlsInsideWorkingAreaWithoutDispatch(
        bool nearRightEdge, bool nearBottomEdge)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(ScratchParent(), silentCaption: true);
            await fixture.InitializeAsync();
            fixture.Main.DefaultResponseMode = ResponseOutputMode.VisualOnly;
            fixture.Main.CommandText = "what power action is pending";
            await fixture.Main.RunTypedCommand.ExecuteAsync();
            fixture.Main.CommandText = "Unsent original input";
            var originalActions = fixture.Main.ResponseActions.ToArray();
            var originalTitle = fixture.Main.ResponseTitle;
            var originalBody = fixture.Main.ResponseBody;
            var captureStarts = fixture.Voice.StartCalls;
            var speechRequests = fixture.SilentSpeech!.AdmittedRequests;
            var window = new ResponseWindow(fixture.Main);
            try
            {
                var screen = window.Screens.Primary
                    ?? throw new InvalidOperationException("The deterministic headless display is unavailable.");
                var area = screen.WorkingArea;
                var saved = new ResponseWindowPosition(
                    nearRightEdge ? area.Right - 1 : area.X + 40,
                    nearBottomEdge ? area.Bottom - 1 : area.Y + 40);
                using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                    HostActivityLayer.Desktop, HostOperation.Policy))
                {
                    (await fixture.Main.SetResponseWindowPositionAsync(saved)).Should().BeTrue();
                }

                window.ShowResponse();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var width = (int)Math.Ceiling(window.Width * window.RenderScaling);
                var height = (int)Math.Ceiling(window.Height * window.RenderScaling);
                width.Should().BeLessThanOrEqualTo(area.Width);
                height.Should().BeLessThanOrEqualTo(area.Height);
                window.Position.Should().Be(new PixelPoint(
                    Math.Min(saved.X, area.Right - width),
                    Math.Min(saved.Y, area.Bottom - height)));
                area.Contains(new PixelRect(window.Position, new PixelSize(width, height))).Should().BeTrue(
                    "the whole response, not only its saved origin, must remain usable");

                var input = window.FindControl<TextBox>("CommandInput")!;
                input.IsEffectivelyVisible.Should().BeTrue();
                input.Text.Should().Be("Unsent original input");
                var inputOrigin = input.TranslatePoint(default, window)!.Value;
                inputOrigin.X.Should().BeGreaterThanOrEqualTo(0);
                inputOrigin.Y.Should().BeGreaterThanOrEqualTo(0);
                (inputOrigin.X + input.Bounds.Width).Should().BeLessThanOrEqualTo(window.Bounds.Width);
                (inputOrigin.Y + input.Bounds.Height).Should().BeLessThanOrEqualTo(window.Bounds.Height);
                input.Text = "Edited without submitting";
                fixture.Main.CommandText.Should().Be("Edited without submitting");

                fixture.Main.ResponseWindowPosition.Should().Be(saved,
                    "recovering placement is not a new saved preference");
                fixture.Main.ResponseActions.Should().Equal(originalActions);
                fixture.Main.ResponseTitle.Should().Be(originalTitle);
                fixture.Main.ResponseBody.Should().Be(originalBody);
                fixture.Voice.StartCalls.Should().Be(captureStarts);
                fixture.SilentSpeech.AdmittedRequests.Should().Be(speechRequests);
                window.ShowActivated.Should().BeFalse();
                window.IsActive.Should().BeFalse();
                window.IsExpanded.Should().BeFalse();
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static string ScratchParent()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kora.slnx")))
        {
            directory = directory.Parent;
        }
        var root = directory?.FullName
            ?? throw new InvalidOperationException("The repository source root is unavailable.");
        return Directory.CreateDirectory(Path.Combine(root, ".scratch", "response-position-tests")).FullName;
    }
}
