using System.Globalization;

using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AllWorkWindowTests
{
    [WindowsFact]
    public async Task AllNineNativeRangesAreAccessibleInclusivePassiveAndRetireRowsSelectionAndContinuation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteTaskControlTests.WaitAsync(f);
            var held = new GatedWorkStore(f.Store);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var service = new SessionWorkspaceService(held, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
            var model = new AllWorkViewModel(service, access, NullLogger<AllWorkViewModel>.Instance,
                (_, _, _) => throw new InvalidOperationException("Range editing cannot navigate."));
            var window = new AllWorkWindow(model);
            window.Show();
            try
            {
                var audits = f.Count("security_audit_events");
                var tasks = f.Count("host_tasks");
                var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                held.Reads.Should().Be(0);
                model.RangeInputs.Should().HaveCount(9).And.OnlyContain(input => input.Minimum.Length == 0 && input.Maximum.Length == 0);
                window.FindControl<Expander>("WorkRanges")!.IsExpanded = true;
                window.UpdateLayout();
                var controls = window.GetVisualDescendants().OfType<TextBox>()
                    .Where(control => control.DataContext is AllWorkViewModel.WorkRangeInput).ToArray();
                controls.Should().HaveCount(18);
                await model.RefreshAsync();
                var work = model.Records.Single().Work!;
                long[] values = [work.AuthorityRevision, work.QueueRevision, work.PendingCount, work.PendingCapacity,
                    work.ExecutionSlots, work.ObservedCurrentFixedReads, work.OmittedQueueRecords, work.OmittedTasks, work.OmittedQuestions];
                for (var index = 0; index < model.RangeInputs.Count; index++)
                {
                    var input = model.RangeInputs[index];
                    var fields = controls.Where(control => ReferenceEquals(control.DataContext, input)).ToArray();
                    fields.Should().HaveCount(2);
                    foreach (var field in fields)
                    {
                        var peer = ControlAutomationPeer.CreatePeerForElement(field)!;
                        peer.GetName().Should().Contain(input.Label).And.Contain("inclusive")
                            .And.Contain("empty means Any").And.Contain("editing performs no read");
                        field.Focusable.Should().BeTrue();
                        field.Focus().Should().BeTrue();
                        model.Select(model.Records.Single());
                        var stale = model.SelectedRecord;
                        var reads = held.Reads;
                        field.Text = values[index].ToString(CultureInfo.InvariantCulture);
                        held.Reads.Should().Be(reads);
                        model.Records.Should().BeEmpty();
                        model.SelectedRecord.Should().BeNull();
                        model.SelectedDetails.Should().BeEmpty();
                        model.CanNext.Should().BeFalse();
                        model.CanNavigate.Should().BeFalse();
                        model.Select(stale);
                        model.CanNavigate.Should().BeFalse();
                        await model.RefreshAsync();
                        model.Records.Should().ContainSingle();
                        field.IsFocused.Should().BeTrue("manual refresh does not steal input focus");
                    }
                }
                model.RangeHint.Should().Contain("Only Observed").And.Contain("never prove absence, completeness, success or authority");
                model.QueryKind = SessionListSearchKind.ExactId;
                model.QueryText = f.Request.SessionId.Value.ToString("D");
                model.NeedsAnswer = true;
                model.Availability = SessionWorkOverviewAvailability.Observed;
                model.Content = SessionWorkOverviewContent.PendingQuestion;
                await model.RefreshAsync();
                model.Records.Single().Work!.PendingQuestions.Should().ContainSingle();
                model.Availability = SessionWorkOverviewAvailability.ReadUnavailable;
                await model.RefreshAsync();
                model.Records.Should().BeEmpty("availability is an independent conjunction");
                held.Fail = true;
                model.Content = SessionWorkOverviewContent.Any;
                await model.RefreshAsync();
                model.Records.Should().BeEmpty("unavailable observations never match ranges");
                var beforeClear = held.Reads;
                window.FindControl<Button>("ClearRanges")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                held.Reads.Should().Be(beforeClear);
                controls.Should().OnlyContain(field => field.Text == "");
                await model.RefreshAsync();
                model.Records.Single().Availability.Should().Be(SessionWorkOverviewAvailability.ReadUnavailable);
                window.Close();
                controls.Should().OnlyContain(field => !field.IsEffectivelyEnabled);
                model.RangeInputs[0].Minimum = "99";
                model.RangeInputs.Should().OnlyContain(input => input.Minimum.Length == 0 && input.Maximum.Length == 0);
                held.Writes.Should().Be(0);
                f.Count("security_audit_events").Should().Be(audits);
                f.Count("host_tasks").Should().Be(tasks);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
            }
            finally { window.Close(); }
        });
    }

    [WindowsFact]
    public async Task InvalidNativeBoundsKeepExplicitRecoveryAndNeverReadOrFallBackToAny()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var held = new GatedWorkStore(f.Store);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var service = new SessionWorkspaceService(held, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
        var model = new AllWorkViewModel(service, access, NullLogger<AllWorkViewModel>.Instance,
            (_, _, _) => throw new InvalidOperationException("Invalid ranges cannot navigate."));
        try
        {
            foreach (var input in model.RangeInputs)
            {
                foreach (var invalid in new[] { "-1", "+1", " 1", "1 ", "1.0", "9223372036854775808" })
                {
                    input.Minimum = invalid;
                    model.CanSearch.Should().BeFalse();
                    await model.RefreshAsync();
                    held.Reads.Should().Be(0);
                    model.Status.Should().Contain("Invalid work metadata range").And.Contain("No read performed");
                    input.Minimum.Should().Be(invalid);
                    model.ClearRanges();
                    input.Maximum = invalid;
                    await model.RefreshAsync();
                    held.Reads.Should().Be(0);
                    model.ClearRanges();
                }
                input.Minimum = "2";
                input.Maximum = "1";
                await model.RefreshAsync();
                held.Reads.Should().Be(0);
                model.CanSearch.Should().BeFalse();
                model.ClearRanges();
            }
            foreach (var index in new[] { 0, 3, 4 })
            {
                model.RangeInputs[index].Maximum = "0";
                await model.RefreshAsync();
                model.CanSearch.Should().BeFalse();
                held.Reads.Should().Be(0);
                model.ClearRanges();
            }
            model.RangeInputs[0].Minimum = "0001";
            model.RangeInputs[1].Maximum = "0";
            await model.RefreshAsync();
            model.Records.Should().ContainSingle("queue revision zero is valid for a session without queue history");
            model.RangeInputs[0].Minimum = "9223372036854775807";
            await model.RefreshAsync();
            model.Records.Should().BeEmpty("a valid but nonmatching bound is not invalid-input recovery");
            model.Status.Should().Contain("not a global all-clear");
        }
        finally { model.Close(); }
    }
}
