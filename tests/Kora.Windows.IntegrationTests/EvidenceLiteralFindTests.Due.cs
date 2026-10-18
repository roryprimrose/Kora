using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;

using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class EvidenceLiteralFindTests
{
    [Theory]
    [InlineData("from", "success")]
    [InlineData("from", "failure")]
    [InlineData("from", "cancel")]
    [InlineData("until", "success")]
    [InlineData("until", "failure")]
    [InlineData("until", "cancel")]
    [InlineData("clear", "success")]
    [InlineData("clear", "failure")]
    [InlineData("clear", "cancel")]
    [InlineData("escape", "success")]
    [InlineData("escape", "failure")]
    [InlineData("escape", "cancel")]
    [InlineData("privacy", "success")]
    [InlineData("privacy", "failure")]
    [InlineData("privacy", "cancel")]
    public async Task NativeDueFieldsAreLabelledReadFreeAndFenceEveryRetiredCompletion(string retirement, string completion)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var listener = WindowsDailyEvidenceReaderTests.Listen();
            var reader = new Reader();
            var access = new Access();
            var viewer = Viewer(reader, access);
            var window = new EvidenceWindow(viewer);
            window.Show();
            try
            {
                window.GetLogicalDescendants().OfType<Expander>().Single().IsExpanded = true;
                window.UpdateLayout();
                var from = window.FindControl<TextBox>("FromDueUtc")!;
                var until = window.FindControl<TextBox>("UntilDueUtc")!;
                foreach (var field in new[] { from, until })
                {
                    field.IsEffectivelyVisible.Should().BeTrue();
                    field.IsEnabled.Should().BeTrue();
                    field.Focusable.Should().BeTrue();
                    field.Focus().Should().BeTrue();
                    ControlAutomationPeer.CreatePeerForElement(field)!.GetName().Should().Contain("retained due UTC");
                }
                window.GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text)
                    .Should().Contain("Retained due UTC from (inclusive)").And.Contain("Retained due UTC until (inclusive)");
                from.Text = "2026-10-10T00:00:00.0000000+00:00";
                until.Text = "2026-10-11T00:00:00.0000000+00:00";
                reader.Queries.Should().BeEmpty();
                await viewer.SearchAsync();
                reader.Queries.Single().FromDueUtc.Should().Be(new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero));
                reader.Queries.Single().UntilDueUtc.Should().Be(new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero));
                viewer.Select(viewer.Records[0]);
                Match(viewer);
                var held = new TaskCompletionSource<EvidenceReadBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
                reader.Read = () => new(held.Task);
                var pending = viewer.NextAsync();
                switch (retirement)
                {
                    case "from": from.Text = ""; break;
                    case "until": until.Text = ""; break;
                    case "clear": Click(window, "ClearAdvanced"); break;
                    case "escape": PressKey(window, Key.Escape); break;
                    case "privacy": access.CanInspect = false; viewer.EnsureFindAdmission().Should().BeFalse(); break;
                    default: throw new ArgumentException("Unknown retirement.", nameof(retirement));
                }
                viewer.Records.Should().BeEmpty();
                viewer.Segments.Should().BeEmpty();
                viewer.ResultText.Should().BeEmpty();
                viewer.CanNext.Should().BeFalse();
                viewer.CanReadTrace.Should().BeFalse();
                reader.Queries.Should().HaveCount(2);
                if (string.Equals(completion, "success", StringComparison.Ordinal)) { held.SetResult(reader.Batch()); }
                else if (string.Equals(completion, "cancel", StringComparison.Ordinal)) { held.SetException(new OperationCanceledException()); }
                else { held.SetException(new IOException("Retired due source failure.")); }
                await pending;
                viewer.Records.Should().BeEmpty();
                viewer.Segments.Should().BeEmpty();
                viewer.ResultText.Should().BeEmpty();
                viewer.CanNext.Should().BeFalse();
                viewer.CanReadTrace.Should().BeFalse();
                Retired(viewer);
                reader.Queries.Should().HaveCount(2);
                if (retirement is "clear" or "escape")
                {
                    from.Text.Should().BeEmpty();
                    until.Text.Should().BeEmpty();
                }
            }
            finally { window.Close(); }
        });
    }
}
