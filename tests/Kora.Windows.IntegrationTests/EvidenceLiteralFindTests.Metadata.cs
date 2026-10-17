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
    [Theory]
    [InlineData("edit", "success")]
    [InlineData("edit", "failure")]
    [InlineData("edit", "cancel")]
    [InlineData("clear", "success")]
    [InlineData("clear", "failure")]
    [InlineData("clear", "cancel")]
    [InlineData("escape", "success")]
    [InlineData("escape", "failure")]
    [InlineData("escape", "cancel")]
    [InlineData("privacy", "success")]
    [InlineData("privacy", "failure")]
    [InlineData("privacy", "cancel")]
    public async Task NativeParentChoiceRetirementSuppressesLateRowsSelectionDetailAndCursor(string retirement, string completion)
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
                var parent = window.FindControl<ComboBox>("TraceParentKind")!;
                parent.SelectedIndex = 1;
                await viewer.SearchAsync();
                reader.Queries.Single().TraceParentKind.Should().Be(TraceParentKind.ObservedRoot);
                viewer.Select(viewer.Records[0]);
                Match(viewer);
                var held = new TaskCompletionSource<EvidenceReadBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
                reader.Read = () => new(held.Task);
                var pending = viewer.NextAsync();
                switch (retirement)
                {
                    case "edit": parent.SelectedIndex = 2; break;
                    case "clear": Click(window, "ClearAdvanced"); break;
                    case "escape": PressKey(window, Key.Escape); break;
                    case "privacy": access.CanInspect = false; viewer.EnsureFindAdmission().Should().BeFalse(); break;
                    default: throw new ArgumentException("Unknown retirement.", nameof(retirement));
                }
                reader.Queries.Should().HaveCount(2);
                if (string.Equals(completion, "success", StringComparison.Ordinal)) { held.SetResult(reader.Batch()); }
                else if (string.Equals(completion, "cancel", StringComparison.Ordinal)) { held.SetException(new OperationCanceledException()); }
                else { held.SetException(new IOException("Retired source failure.")); }
                await pending;
                viewer.Records.Should().BeEmpty();
                viewer.Segments.Should().BeEmpty();
                viewer.ResultText.Should().BeEmpty();
                viewer.CanReadTrace.Should().BeFalse();
                viewer.CanNext.Should().BeFalse();
                Retired(viewer);
                if (string.Equals(retirement, "edit", StringComparison.Ordinal))
                {
                    window.UpdateLayout();
                    parent.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).Should().Contain("Child");
                }
                if (retirement is "clear" or "escape") { parent.SelectedIndex.Should().Be(0); }
                reader.Queries.Should().HaveCount(2);
            }
            finally { window.Close(); }
        });
    }

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
                var parentKind = window.FindControl<ComboBox>("TraceParentKind")!;
                var outcome = window.FindControl<ComboBox>("OperationOutcome")!;
                var content = window.FindControl<ComboBox>("ContentAvailability")!;
                var eventName = window.FindControl<TextBox>("EventName")!;
                var exceptionType = window.FindControl<TextBox>("ExceptionType")!;
                var fromSequence = window.FindControl<TextBox>("FromAuditSequence")!;
                var untilSequence = window.FindControl<TextBox>("UntilAuditSequence")!;
                var traceSource = window.FindControl<TextBox>("TraceSource")!;
                var version = window.FindControl<TextBox>("TraceSourceVersion")!;
                var availability = window.FindControl<ComboBox>("TraceVersionAvailability")!;
                var activityName = window.FindControl<TextBox>("ActivityName")!;
                var kind = window.FindControl<ComboBox>("ActivityKind")!;
                var flags = window.FindControl<ComboBox>("TraceFlags")!;
                foreach (var control in new Control[] { parent, outcome, content, eventName, exceptionType, fromSequence, untilSequence,
                    traceSource, version, availability, activityName, kind, flags, parentKind })
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
                foreach (var choice in new[] { availability, kind, flags, parentKind })
                {
                    choice.SelectedItem.Should().NotBeNull();
                    choice.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).Should().Contain("Any");
                    choice.Focus();
                    PressKey(choice, Key.Down);
                    choice.SelectedIndex.Should().Be(1);
                }
                viewer.TraceVersionAvailability.Should().Be(TraceSourceVersionAvailability.Absent);
                viewer.ActivityKind.Value.Should().Be(System.Diagnostics.ActivityKind.Internal);
                viewer.TraceFlags.Value.Should().Be(System.Diagnostics.ActivityTraceFlags.None);
                viewer.ParentKind.Value.Should().Be(TraceParentKind.ObservedRoot);
                parentKind.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).Should().Contain("Root");
                traceSource.Text = "Kora.Core";
                activityName.Text = "request";
                availability.SelectedIndex = 3;
                version.Text = "1";
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
                query.TraceParentKind.Should().Be(TraceParentKind.ObservedRoot);
                query.Outcome.Should().Be(HostOperationOutcome.Completed);
                query.ContentAvailability.Should().Be(EvidenceContentAvailability.Present);
                query.EventName.Should().Be(eventName.Text);
                query.ExceptionType.Should().Be(exceptionType.Text);
                query.FromAuditSequence.Should().Be(1);
                query.UntilAuditSequence.Should().Be(long.MaxValue);
                query.TraceSource.Should().Be(traceSource.Text);
                query.TraceSourceVersion.Should().Be(version.Text);
                query.TraceSourceVersionAvailability.Should().Be(TraceSourceVersionAvailability.Exact);
                query.ActivityName.Should().Be(activityName.Text);
                query.ActivityKind.Should().Be(System.Diagnostics.ActivityKind.Internal);
                query.TraceFlags.Should().Be(System.Diagnostics.ActivityTraceFlags.None);
                Click(window, "ClearAdvanced");
                parent.Text.Should().BeEmpty();
                outcome.SelectedIndex.Should().Be(0);
                content.SelectedIndex.Should().Be(0);
                eventName.Text.Should().BeEmpty();
                exceptionType.Text.Should().BeEmpty();
                fromSequence.Text.Should().BeEmpty();
                untilSequence.Text.Should().BeEmpty();
                traceSource.Text.Should().BeEmpty();
                version.Text.Should().BeEmpty();
                activityName.Text.Should().BeEmpty();
                availability.SelectedIndex.Should().Be(0);
                kind.SelectedIndex.Should().Be(0);
                flags.SelectedIndex.Should().Be(0);
                parentKind.SelectedIndex.Should().Be(0);
                reader.Queries.Should().HaveCount(2);
                await viewer.SearchAsync();
                reader.Queries[^1].ParentSpanId.Should().BeNull();
                reader.Queries[^1].TraceParentKind.Should().Be(TraceParentKind.Any);
                reader.Queries[^1].Outcome.Should().BeNull();
                reader.Queries[^1].ContentAvailability.Should().Be(EvidenceContentAvailability.Any);
                reader.Queries[^1].EventName.Should().BeNull();
                reader.Queries[^1].ExceptionType.Should().BeNull();
                reader.Queries[^1].FromAuditSequence.Should().BeNull();
                reader.Queries[^1].UntilAuditSequence.Should().BeNull();
                reader.Queries[^1].TraceSource.Should().BeNull();
                reader.Queries[^1].TraceSourceVersion.Should().BeNull();
                reader.Queries[^1].TraceSourceVersionAvailability.Should().Be(TraceSourceVersionAvailability.Any);
                reader.Queries[^1].ActivityName.Should().BeNull();
                reader.Queries[^1].ActivityKind.Should().BeNull();
                reader.Queries[^1].TraceFlags.Should().BeNull();
            }
            finally { window.Close(); }
        });
    }
}
