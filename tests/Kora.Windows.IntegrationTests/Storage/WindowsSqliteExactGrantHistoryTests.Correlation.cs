using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteExactGrantHistoryTests
{
    [WindowsFact]
    public async Task CorrelationFiltersObserveCommittedIdsNotMembershipOrRequestedReferencesAndNeverWrite()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grants = await Grants(f, 2);
        await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grants[0].Id, grants[0].Revision, f.Token));
        var selection = new ExactGrantBatch(Guid.NewGuid(), (await f.Store.ReadExactGrantPageAsync(null, f.Token)).Records);
        var preview = await f.Store.PreviewExactGrantBatchAsync(selection, f.Token);
        await using var control = Control(f);
        await control.RevokeBatchAsync(preview, static () => true, f.Token);
        var exact = (await f.Store.InspectExactGrantAsync(grants[0].Id, f.Token))!;
        var before = SHA256.HashData(File.ReadAllBytes(f.DatabasePath));
        var all = await ReadFiltered(f, exact, ExactGrantHistoryQuery.Unfiltered);
        all.Records.Should().Contain(row => row.Audit.ApprovalId == null)
            .And.Contain(row => row.Audit.ApprovalId != null);
        all.Records[0].Audit.ApprovalId.Should().Be(grants[0].Id.Value);
        foreach (var row in all.Records)
        {
            foreach (var query in new[]
            {
                new ExactGrantHistoryQuery(auditCorrelationId: row.Audit.CorrelationId),
                new ExactGrantHistoryQuery(approval: ExactGrantHistoryApproval.Present),
                new ExactGrantHistoryQuery(approval: ExactGrantHistoryApproval.Absent),
                new ExactGrantHistoryQuery(approval: ExactGrantHistoryApproval.Exact,
                    approvalId: row.Audit.ApprovalId ?? grants[0].Id.Value),
                new ExactGrantHistoryQuery(row.Audit.Outcome, row.Audit.ReasonCode, row.Audit.Initiator,
                    row.Retention, row.CommittedAt, row.CommittedAt, row.DueAt, row.DueAt,
                    row.Audit.CorrelationId, row.Audit.ApprovalId is null ? ExactGrantHistoryApproval.Absent : ExactGrantHistoryApproval.Exact,
                    row.Audit.ApprovalId),
            })
            {
                var page = await ReadFiltered(f, exact, query);
                page.Records.Select(entry => entry.Audit.CorrelationId).Should()
                    .Equal(all.Records.Where(query.Matches).Select(entry => entry.Audit.CorrelationId));
                page.NonmatchingRows.Should().Be(page.ScannedRows - page.Records.Length);
                page.Current.Should().Be(exact);
                page.AuditCeilingDigest.Should().Be(all.AuditCeilingDigest);
            }
        }
        var batch = all.Records.Single(row => row.Audit.ApprovalId is null);
        batch.Provenance.RequestedGrants.Should().NotBeEmpty();
        (await ReadFiltered(f, exact, new(auditCorrelationId: batch.Audit.CorrelationId,
            approval: ExactGrantHistoryApproval.Exact, approvalId: grants[0].Id.Value))).Records.Should().BeEmpty();
        var other = (await f.Store.InspectExactGrantAsync(grants[1].Id, f.Token))!;
        (await ReadFiltered(f, other, new(auditCorrelationId: all.Records[0].Audit.CorrelationId))).Records.Should().BeEmpty();
        SHA256.HashData(File.ReadAllBytes(f.DatabasePath)).Should().Equal(before);
    }

    [WindowsFact]
    public async Task ZeroMatchCursorBindsEveryCorrelationPredicateAndReplaysOnlyTheSameFiniteQuery()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        AppendUnrelated(f, 4200);
        var exact = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        var viewer = HostRequest.Create(RequestOrigin.LocalUi);
        var correlation = Guid.NewGuid();
        var approval = Guid.NewGuid();
        var query = new ExactGrantHistoryQuery(auditCorrelationId: correlation,
            approval: ExactGrantHistoryApproval.Exact, approvalId: approval);
        var first = await ReadFiltered(f, exact, query, viewer);
        first.Records.Should().BeEmpty();
        first.ScannedRows.Should().Be(4096);
        first.NonmatchingRows.Should().Be(4096);
        first.Next.Should().NotBeNull();
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(first.Next!.Split('.')[0]));
        payload.Should().NotContain(correlation.ToString("D")).And.NotContain(approval.ToString("D"));
        foreach (var changed in new[]
        {
            new ExactGrantHistoryQuery(approval: ExactGrantHistoryApproval.Exact, approvalId: approval),
            new ExactGrantHistoryQuery(auditCorrelationId: Guid.NewGuid(), approval: ExactGrantHistoryApproval.Exact, approvalId: approval),
            new ExactGrantHistoryQuery(auditCorrelationId: correlation),
            new ExactGrantHistoryQuery(auditCorrelationId: correlation, approval: ExactGrantHistoryApproval.Present),
            new ExactGrantHistoryQuery(auditCorrelationId: correlation, approval: ExactGrantHistoryApproval.Absent),
            new ExactGrantHistoryQuery(auditCorrelationId: correlation, approval: ExactGrantHistoryApproval.Exact, approvalId: Guid.NewGuid()),
        })
        {
            var read = () => ReadFiltered(f, exact, changed, viewer, first.Next);
            await read.Should().ThrowAsync<InvalidDataException>();
        }
        var last = await ReadFiltered(f, exact, query, viewer, first.Next);
        last.Records.Should().BeEmpty();
        last.Next.Should().BeNull();
        last.ScannedThrough.Should().Be(first.AuditCeiling);
        last.AuditCeilingDigest.Should().Be(first.AuditCeilingDigest);
    }

    [Theory]
    [InlineData("UPDATE security_audit_events SET envelope=json_set(envelope,'$.Audit.ApprovalId','00000000-0000-0000-0000-000000000000') WHERE sequence=1;")]
    [InlineData("UPDATE security_audit_events SET envelope=json_set(envelope,'$.SessionGeneration',0) WHERE sequence=1;")]
    [InlineData("UPDATE security_audit_events SET envelope=json_set(envelope,'$.Changes',json('[]')) WHERE sequence=1;")]
    public async Task NonmatchingCorrelationNeverHidesInvalidAuditOrProvenance(string corruption)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        var exact = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        f.Mutate(corruption);
        RehashCorrelationFixture(f);
        var read = () => ReadFiltered(f, exact, new(auditCorrelationId: Guid.NewGuid(), approval: ExactGrantHistoryApproval.Absent));
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    private static void RehashCorrelationFixture(InteractionStorageFixture f)
    {
        using var connection = f.OpenRaw();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT sequence,envelope FROM security_audit_events ORDER BY sequence;";
        var rows = new List<(long Sequence, string Envelope)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read()) { rows.Add((reader.GetInt64(0), reader.GetString(1))); }
        }
        var previous = new string('0', 64);
        foreach (var row in rows)
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                row.Sequence.ToString(CultureInfo.InvariantCulture) + "\n" + previous + "\n" + row.Envelope)));
            command.Parameters.Clear();
            command.CommandText = "UPDATE security_audit_events SET previous_hash=$previous,hash=$hash WHERE sequence=$sequence;";
            command.Parameters.AddWithValue("$previous", previous);
            command.Parameters.AddWithValue("$hash", hash);
            command.Parameters.AddWithValue("$sequence", row.Sequence);
            command.ExecuteNonQuery();
            previous = hash;
        }
        command.Parameters.Clear();
        command.CommandText = "UPDATE authority_head SET hash=$hash WHERE singleton=1;";
        command.Parameters.AddWithValue("$hash", previous);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    [WindowsFact]
    public async Task NativeCorrelationControlsHaveExplicitDefaultsKeyboardAccessPassiveEditsAndClear()
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
                var exact = (await f.Store.InspectExactGrantAsync(state.Records[0].Grant.Id, f.Token))!;
                var row = (await ReadFiltered(f, exact, ExactGrantHistoryQuery.Unfiltered)).Records[0];
                var correlation = window.GetVisualDescendants().OfType<TextBox>().Single(item =>
                    AutomationProperties.GetName(item)?.StartsWith("Exact grant history committed audit correlation ID", StringComparison.Ordinal) == true);
                var choice = window.GetVisualDescendants().OfType<ComboBox>().Single(item =>
                    AutomationProperties.GetName(item)?.StartsWith("Exact grant history committed approval metadata choice", StringComparison.Ordinal) == true);
                var approval = window.GetVisualDescendants().OfType<TextBox>().Single(item =>
                    AutomationProperties.GetName(item)?.StartsWith("Exact grant history committed approval ID", StringComparison.Ordinal) == true);
                choice.Items.Cast<string>().Should().Equal(ExactGrantHistoryQuery.ApprovalOptions);
                choice.SelectedItem.Should().Be("Any");
                correlation.Text.Should().BeEmpty();
                approval.Text.Should().BeEmpty();
                correlation.Focusable.Should().BeTrue();
                choice.Focusable.Should().BeTrue();
                approval.Focusable.Should().BeTrue();
                correlation.Focus();
                correlation.IsFocused.Should().BeTrue();
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                choice.IsFocused.Should().BeTrue();
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                approval.IsFocused.Should().BeTrue();
                window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
                choice.IsFocused.Should().BeTrue();
                var before = SHA256.HashData(File.ReadAllBytes(f.DatabasePath));
                correlation.Text = row.Audit.CorrelationId.ToString("D");
                choice.SelectedItem = "Exact";
                approval.Text = row.Audit.ApprovalId!.Value.ToString("D");
                state.HistoryAuditCorrelationId.Should().Be(correlation.Text);
                state.HistoryApprovalFilter.Should().Be("Exact");
                state.HistoryApprovalId.Should().Be(approval.Text);
                state.HistoryRows.Should().BeEmpty();
                state.CanReadHistory.Should().BeTrue();
                state.CanRevoke.Should().BeFalse();
                await state.ReadHistoryCommand.ExecuteAsync();
                state.HistoryRows.Should().ContainSingle();
                SHA256.HashData(File.ReadAllBytes(f.DatabasePath)).Should().Equal(before);
                await state.ClearHistoryFiltersCommand.ExecuteAsync();
                correlation.Text.Should().BeEmpty();
                choice.SelectedItem.Should().Be("Any");
                approval.Text.Should().BeEmpty();
                state.HistoryRows.Should().BeEmpty();
                state.CanReadHistory.Should().BeTrue();
            }
            finally { window.Close(); }
        });
    }
}
