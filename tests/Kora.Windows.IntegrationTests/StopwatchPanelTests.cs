using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Timekeeping;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class StopwatchPanelTests
{
    [WindowsFact]
    public async Task NativeAccessiblePanelUsesSharedWorkflowAndRetiresClosedPresenter()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            var time = new PanelTime();
            using var workflow = new StopwatchWorkflow(time, NullLogger<StopwatchWorkflow>.Instance);
            using var fixture = new NativeUxFixtureSession(parent, silentCaption: true, stopwatch: workflow);
            await fixture.InitializeAsync();
            var main = fixture.Main;
            var window = new ResponseWindow(main);
            window.Show();
            main.CommandText = "stopwatch start";
            await main.RunTypedCommand.ExecuteAsync();
            main.StopwatchDisplay.Should().StartWith("Running");
            time.Timestamp = 7654321;
            main.StopwatchDisplay.Should().Contain("00:00:00");
            var status = window.GetVisualDescendants().OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, main.StopwatchStatusCommand));
            AutomationProperties.GetName(status).Should().Be("Observe stopwatch elapsed now");
            var stop = window.GetVisualDescendants().OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, main.StopwatchStopCommand));
            AutomationProperties.GetName(stop).Should().Be("Stop stopwatch and freeze elapsed");
            var reset = window.GetVisualDescendants().OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, main.StopwatchResetCommand));
            AutomationProperties.GetName(reset).Should().Be("Discard stopwatch run and result");
            await main.StopwatchStatusCommand.ExecuteAsync();
            main.StopwatchDisplay.Should().Contain("00:00:00.7654321");
            await main.StopwatchStopCommand.ExecuteAsync();
            main.StopwatchDisplay.Should().StartWith("Stopped");
            await main.StopwatchResetCommand.ExecuteAsync();
            main.StopwatchDisplay.Should().StartWith("Idle");
            main.CommandText = "stopwatch start";
            await main.RunTypedCommand.ExecuteAsync();
            window.Close();
            main.StopwatchDisplay.Should().StartWith("Idle");
            await main.StopwatchStopCommand.ExecuteAsync();
            main.ResponseBody.Should().Contain("native lifetime admission was denied");
        });
    }

    private sealed class PanelTime : TimeProvider
    {
        internal long Timestamp { get; set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Timestamp;
    }
}
