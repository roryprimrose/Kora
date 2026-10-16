using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class EvidenceLiteralFindTests
{
    [Fact]
    public async Task NativeMetadataControlsOfferVisibleAccessibleAnyAndKeyboardChoicesWithoutReading()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var listener = WindowsDailyEvidenceReaderTests.Listen();
            var reader = new Reader();
            var viewer = Viewer(reader);
            var window = new EvidenceWindow(viewer);
            window.Show();
            try
            {
                window.GetLogicalDescendants().OfType<Expander>().Single().IsExpanded = true;
                window.UpdateLayout();
                var filters = window.GetLogicalDescendants().OfType<ScrollViewer>()
                    .Single(control => control.MaxHeight == 280);
                filters.Bounds.Height.Should().BeLessThanOrEqualTo(280);
                filters.VerticalScrollBarVisibility.Should().Be(Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
                var parent = window.FindControl<TextBox>("ParentSpanId")!;
                var outcome = window.FindControl<ComboBox>("OperationOutcome")!;
                var content = window.FindControl<ComboBox>("ContentAvailability")!;
                var eventName = window.FindControl<TextBox>("EventName")!;
                var exceptionType = window.FindControl<TextBox>("ExceptionType")!;
                var fromSequence = window.FindControl<TextBox>("FromAuditSequence")!;
                var untilSequence = window.FindControl<TextBox>("UntilAuditSequence")!;
                foreach (var control in new Control[] { parent, outcome, content, eventName, exceptionType, fromSequence, untilSequence })
                {
                    control.IsEffectivelyVisible.Should().BeTrue();
                    control.IsEnabled.Should().BeTrue();
                    control.Focusable.Should().BeTrue();
                    control.Focus().Should().BeTrue();
                    control.Bounds.Width.Should().BeGreaterThan(0);
                    ControlAutomationPeer.CreatePeerForElement(control)!.GetName().Should().NotBeNullOrWhiteSpace();
                }
                outcome.SelectedItem.Should().Be(EvidenceViewModel.OutcomeChoices[0]);
                content.SelectedItem.Should().Be(EvidenceContentAvailability.Any);
                outcome.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).Should().Contain("Any");
                content.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).Should().Contain("Any");
                await viewer.SearchAsync();
                viewer.Select(viewer.Records[0]);
                Match(viewer);
                parent.Text = "0123456789abcdef";
                viewer.ParentSpanFilter.Should().Be(parent.Text);
                Retired(viewer);
                viewer.Records.Should().BeEmpty();
                viewer.CanNext.Should().BeFalse();
                outcome.Focus();
                PressKey(outcome, Key.Down);
                outcome.SelectedIndex.Should().Be(1);
                viewer.Outcome.Value.Should().Be(HostOperationOutcome.Completed);
                content.Focus();
                PressKey(content, Key.Down);
                content.SelectedIndex.Should().Be(1);
                viewer.ContentAvailability.Should().Be(EvidenceContentAvailability.Present);
                reader.Queries.Should().ContainSingle();
                eventName.Text = "Event.Name";
                exceptionType.Text = "System.IOException";
                fromSequence.Text = "1";
                untilSequence.Text = "9223372036854775807";
                await viewer.SearchAsync();
                var query = reader.Queries[^1];
                query.ParentSpanId.Should().Be(parent.Text);
                query.Outcome.Should().Be(HostOperationOutcome.Completed);
                query.ContentAvailability.Should().Be(EvidenceContentAvailability.Present);
                query.EventName.Should().Be(eventName.Text);
                query.ExceptionType.Should().Be(exceptionType.Text);
                query.FromAuditSequence.Should().Be(1);
                query.UntilAuditSequence.Should().Be(long.MaxValue);
                Click(window, "ClearAdvanced");
                parent.Text.Should().BeEmpty();
                outcome.SelectedIndex.Should().Be(0);
                content.SelectedIndex.Should().Be(0);
                eventName.Text.Should().BeEmpty();
                exceptionType.Text.Should().BeEmpty();
                fromSequence.Text.Should().BeEmpty();
                untilSequence.Text.Should().BeEmpty();
                reader.Queries.Should().HaveCount(2);
                await viewer.SearchAsync();
                reader.Queries[^1].ParentSpanId.Should().BeNull();
                reader.Queries[^1].Outcome.Should().BeNull();
                reader.Queries[^1].ContentAvailability.Should().Be(EvidenceContentAvailability.Any);
                reader.Queries[^1].EventName.Should().BeNull();
                reader.Queries[^1].ExceptionType.Should().BeNull();
                reader.Queries[^1].FromAuditSequence.Should().BeNull();
                reader.Queries[^1].UntilAuditSequence.Should().BeNull();
            }
            finally { window.Close(); }
        });
    }
}
