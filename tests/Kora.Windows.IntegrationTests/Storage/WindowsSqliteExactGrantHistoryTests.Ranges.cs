using System.Globalization;
using System.Security.Cryptography;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteExactGrantHistoryTests
{
    [WindowsFact]
    public async Task CompleteCommittedEnvelopesMatchInclusiveRangesAndRetentionWithoutWritesOrProvenanceChanges()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        var inspected = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        await using var control = Control(f);
        f.Time.Now = f.Time.Now.AddDays(1);
        await control.RevokeAsync(inspected, static () => true, f.Token);
        await control.RevokeAsync(inspected, static () => true, f.Token);
        inspected = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        f.Time.Now = f.Time.Now.AddDays(89);
        var before = SHA256.HashData(File.ReadAllBytes(f.DatabasePath));
        var all = await ReadFiltered(f, inspected, ExactGrantHistoryQuery.Unfiltered);
        all.Records.Select(row => row.Retention).Should().Contain(EvidenceSegmentStatus.Present)
            .And.Contain(EvidenceSegmentStatus.ExpiredButPresent);
        foreach (var row in all.Records)
        {
            foreach (var query in new[]
            {
                new ExactGrantHistoryQuery(retention: row.Retention),
                new ExactGrantHistoryQuery(committedFromUtc: row.CommittedAt, committedUntilUtc: row.CommittedAt),
                new ExactGrantHistoryQuery(dueFromUtc: row.DueAt, dueUntilUtc: row.DueAt),
                new ExactGrantHistoryQuery(row.Audit.Outcome, row.Audit.ReasonCode, row.Audit.Initiator,
                    row.Retention, row.CommittedAt, row.CommittedAt, row.DueAt, row.DueAt),
                new ExactGrantHistoryQuery(committedFromUtc: row.CommittedAt.AddTicks(1)),
                new ExactGrantHistoryQuery(committedUntilUtc: row.CommittedAt.AddTicks(-1)),
                new ExactGrantHistoryQuery(dueFromUtc: row.DueAt.AddTicks(1)),
                new ExactGrantHistoryQuery(dueUntilUtc: row.DueAt.AddTicks(-1)),
            })
            {
                var page = await ReadFiltered(f, inspected, query);
                page.Records.Select(entry => ExactGrantHistoryPage.Serialize(page with { Records = [entry] }))
                    .Select(Convert.ToHexString).Should().Equal(all.Records.Where(query.Matches)
                        .Select(entry => ExactGrantHistoryPage.Serialize(page with { Records = [entry] })).Select(Convert.ToHexString));
                page.NonmatchingRows.Should().Be(page.ScannedRows - page.Records.Length);
                page.AuditCeiling.Should().Be(all.AuditCeiling);
                page.AuditCeilingDigest.Should().Be(all.AuditCeilingDigest);
                page.Current.Should().Be(inspected);
            }
        }
        SHA256.HashData(File.ReadAllBytes(f.DatabasePath)).Should().Equal(before);
    }

    [WindowsFact]
    public async Task NativeRetentionAndUtcFieldsHaveExplicitDefaultsKeyboardAccessAndNoReadEdits()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await f.GrantAsync("perpetual");
        await using var control = Control(f);
        await HeadlessSession.RunAsync(async () =>
        {
            using var state = new ExactGrantsViewModel(f.Store, control, new Access(), static () => true,
                NullLogger<ExactGrantsViewModel>.Instance);
            var window = new ExactGrantsWindow(state);
            try
            {
                window.Show();
                await state.RefreshAsync(f.Token);
                state.Selected = state.Records[0];
                await state.InspectAsync(f.Token);
                await state.ReadHistoryAsync(f.Token);
                var retention = window.GetVisualDescendants().OfType<ComboBox>().Single(item =>
                    AutomationProperties.GetName(item)?.StartsWith("Exact grant history retention state", StringComparison.Ordinal) == true);
                retention.Items.Cast<string>().Should().Equal(ExactGrantHistoryQuery.RetentionOptions);
                retention.SelectedItem.Should().Be("Any");
                retention.Focusable.Should().BeTrue();
                retention.Focus();
                retention.IsFocused.Should().BeTrue();
                var fields = window.GetVisualDescendants().OfType<TextBox>().Where(item =>
                    AutomationProperties.GetName(item)?.Contains("canonical UTC inclusive", StringComparison.Ordinal) == true).ToArray();
                fields.Should().HaveCount(4);
                foreach (var field in fields)
                {
                    field.Focusable.Should().BeTrue();
                    field.Text.Should().BeEmpty();
                }
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                fields[0].IsFocused.Should().BeTrue();
                foreach (var field in fields.Skip(1))
                {
                    window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                    field.IsFocused.Should().BeTrue();
                }
                window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
                fields[2].IsFocused.Should().BeTrue();
                var before = SHA256.HashData(File.ReadAllBytes(f.DatabasePath));
                var date = f.Time.Now.ToString("O", CultureInfo.InvariantCulture);
                retention.SelectedItem = "Present";
                fields[0].Text = date;
                fields[1].Text = date;
                fields[2].Text = f.Time.Now.AddDays(90).ToString("O", CultureInfo.InvariantCulture);
                fields[3].Text = fields[2].Text;
                state.HistoryRetentionFilter.Should().Be("Present");
                state.HistoryCommittedFromUtc.Should().Be(date);
                state.HistoryCommittedUntilUtc.Should().Be(date);
                state.HistoryDueFromUtc.Should().Be(fields[2].Text);
                state.HistoryDueUntilUtc.Should().Be(fields[2].Text);
                state.HistoryRows.Should().BeEmpty();
                state.CanReadHistory.Should().BeTrue();
                state.CanRevoke.Should().BeFalse();
                state.HistorySummary.Should().Contain("typing performs no read");
                await state.ReadHistoryCommand.ExecuteAsync();
                state.HistoryRows.Should().ContainSingle();
                SHA256.HashData(File.ReadAllBytes(f.DatabasePath)).Should().Equal(before);
                await state.ClearHistoryFiltersCommand.ExecuteAsync();
                retention.SelectedItem.Should().Be("Any");
                fields.Should().OnlyContain(field => field.Text == "");
                state.HistoryRows.Should().BeEmpty();
                state.CanReadHistory.Should().BeTrue();
            }
            finally { window.Close(); }
        });
    }
}
