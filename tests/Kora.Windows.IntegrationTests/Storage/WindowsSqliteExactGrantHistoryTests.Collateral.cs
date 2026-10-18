using System.Security.Cryptography;

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
    public async Task CollateralRangesMatchValidatedBatchCountsWithoutDisclosingOtherReferencesOrWriting()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grants = await Grants(f, 3);
        var selection = new ExactGrantBatch(Guid.NewGuid(), (await f.Store.ReadExactGrantPageAsync(null, f.Token)).Records);
        var preview = await f.Store.PreviewExactGrantBatchAsync(selection, f.Token);
        await using var control = Control(f);
        await control.RevokeBatchAsync(preview, static () => true, f.Token);
        var exact = (await f.Store.InspectExactGrantAsync(grants[0].Id, f.Token))!;
        var before = SHA256.HashData(File.ReadAllBytes(f.DatabasePath));
        var all = await ReadFiltered(f, exact, ExactGrantHistoryQuery.Unfiltered);
        var batch = all.Records.Single(row => row.OtherRequestedGrants == 2);
        batch.OtherChanges.Should().Be(2);
        batch.Provenance.Changes.Should().ContainSingle();
        batch.Provenance.RequestedGrants.Should().ContainSingle();
        foreach (var query in new[]
        {
            new ExactGrantHistoryQuery(minimumOtherChanges: 2, maximumOtherChanges: 2,
                minimumOtherRequestedGrants: 2, maximumOtherRequestedGrants: 2),
            new ExactGrantHistoryQuery(minimumOtherChanges: 3),
            new ExactGrantHistoryQuery(maximumOtherChanges: 1),
            new ExactGrantHistoryQuery(minimumOtherRequestedGrants: 3),
            new ExactGrantHistoryQuery(maximumOtherRequestedGrants: 1),
            new ExactGrantHistoryQuery(minimumOtherChanges: 0, maximumOtherRequestedGrants: int.MaxValue),
            new ExactGrantHistoryQuery(batch.Audit.Outcome, batch.Audit.ReasonCode, batch.Audit.Initiator,
                batch.Retention, batch.CommittedAt, batch.CommittedAt, batch.DueAt, batch.DueAt,
                batch.Audit.CorrelationId, ExactGrantHistoryApproval.Absent,
                sessionId: batch.Request.SessionId.Value, requestId: batch.Request.RequestId.Value,
                taskId: batch.Request.TaskId.Value, minimumOtherChanges: 2, maximumOtherChanges: 2,
                minimumOtherRequestedGrants: 2, maximumOtherRequestedGrants: 2),
        })
        {
            var page = await ReadFiltered(f, exact, query);
            page.Records.Select(row => row.Provenance.CommitDigest).Should()
                .Equal(all.Records.Where(query.Matches).Select(row => row.Provenance.CommitDigest));
            page.NonmatchingRows.Should().Be(page.ScannedRows - page.Records.Length);
            foreach (var row in page.Records)
            {
                row.Provenance.Changes.Should().OnlyContain(change => change.Id == exact.Grant.Id.Value.ToString("D"));
                (row.Provenance.RequestedGrants ?? []).Should().OnlyContain(change => change.Id == exact.Grant.Id.Value.ToString("D"));
            }
        }
        SHA256.HashData(File.ReadAllBytes(f.DatabasePath)).Should().Equal(before);
    }

    [WindowsFact]
    public async Task EveryCollateralBoundAuthenticatesContinuationIncludingDefaultAndZeroMatchPages()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        AppendUnrelated(f, 4200);
        var grant = await f.GrantAsync("perpetual");
        var exact = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        var viewer = HostRequest.Create(RequestOrigin.LocalUi);
        var query = new ExactGrantHistoryQuery(minimumOtherChanges: 0, maximumOtherChanges: int.MaxValue,
            minimumOtherRequestedGrants: 0, maximumOtherRequestedGrants: int.MaxValue);
        var first = await ReadFiltered(f, exact, query, viewer);
        first.Records.Should().BeEmpty();
        first.ScannedRows.Should().Be(4096);
        first.Next.Should().NotBeNull();
        foreach (var changed in new[]
        {
            ExactGrantHistoryQuery.Unfiltered,
            new ExactGrantHistoryQuery(maximumOtherChanges: int.MaxValue, minimumOtherRequestedGrants: 0, maximumOtherRequestedGrants: int.MaxValue),
            new ExactGrantHistoryQuery(minimumOtherChanges: 0, minimumOtherRequestedGrants: 0, maximumOtherRequestedGrants: int.MaxValue),
            new ExactGrantHistoryQuery(minimumOtherChanges: 0, maximumOtherChanges: int.MaxValue, maximumOtherRequestedGrants: int.MaxValue),
            new ExactGrantHistoryQuery(minimumOtherChanges: 0, maximumOtherChanges: int.MaxValue, minimumOtherRequestedGrants: 0),
            new ExactGrantHistoryQuery(minimumOtherChanges: 1, maximumOtherChanges: int.MaxValue,
                minimumOtherRequestedGrants: 0, maximumOtherRequestedGrants: int.MaxValue),
        })
        {
            var read = () => ReadFiltered(f, exact, changed, viewer, first.Next);
            await read.Should().ThrowAsync<InvalidDataException>();
        }
        var second = await ReadFiltered(f, exact, query, viewer, first.Next);
        second.Records.Should().ContainSingle();
        second.Next.Should().BeNull();
        second.ScannedThrough.Should().Be(first.AuditCeiling);
        second.AuditCeilingDigest.Should().Be(first.AuditCeilingDigest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptLateEnvelopeFailsEvenBeyondFullSerializedResultBudget(bool rehash)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        for (var index = 0; index < 60; index++)
        {
            grant = (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, f.Token))).Grant!;
        }
        var exact = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        var query = new ExactGrantHistoryQuery(minimumOtherChanges: 0);
        var prefix = await ReadFiltered(f, exact, query);
        AppendUnrelated(f, 1);
        var valid = await ReadFiltered(f, exact, query);
        valid.Next.Should().NotBeNull();
        valid.ScannedRows.Should().BeGreaterThan(valid.Records.Length);
        valid.ScannedThrough.Should().BeLessThan(valid.AuditCeiling);
        valid.NonmatchingRows.Should().Be(prefix.NonmatchingRows + 1);
        valid.Records.Select(row => row.Provenance.CommitDigest).Should().Equal(prefix.Records.Select(row => row.Provenance.CommitDigest));
        f.Mutate("""
            UPDATE security_audit_events SET envelope=json_set(envelope,'$.Changes[0].Kind','undefined')
            WHERE sequence=(SELECT max(sequence) FROM security_audit_events);
            """);
        if (rehash) { RehashCorrelationFixture(f); }
        var read = () => ReadFiltered(f, exact, query);
        await read.Should().ThrowAsync<InvalidDataException>();
        var excluded = () => ReadFiltered(f, exact, new(minimumOtherChanges: int.MaxValue));
        await excluded.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task NativeCollateralMinMaxFieldsAreAccessiblePassiveAndEscapeRetiresSelectionAndReviews()
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
                var fields = window.GetVisualDescendants().OfType<TextBox>().Where(item =>
                    AutomationProperties.GetName(item)?.StartsWith("Exact grant history other ", StringComparison.Ordinal) == true).ToArray();
                fields.Should().HaveCount(4);
                fields.Should().OnlyContain(field => field.Text == "" && field.Focusable);
                var before = SHA256.HashData(File.ReadAllBytes(f.DatabasePath));
                fields[0].Focus();
                fields[0].IsFocused.Should().BeTrue();
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                fields[1].IsFocused.Should().BeTrue();
                window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
                fields[0].IsFocused.Should().BeTrue();
                foreach (var field in fields)
                {
                    field.Text = "0";
                    state.HistoryRows.Should().BeEmpty();
                    state.Selected.Should().BeNull();
                    state.Reviewed.Should().BeFalse();
                    state.BatchReviewed.Should().BeFalse();
                    state.CanReadHistory.Should().BeTrue();
                    state.HistorySummary.Should().Contain("typing performs no read");
                }
                state.HistoryMinimumOtherChanges.Should().Be("0");
                state.HistoryMaximumOtherChanges.Should().Be("0");
                state.HistoryMinimumOtherRequestedGrants.Should().Be("0");
                state.HistoryMaximumOtherRequestedGrants.Should().Be("0");
                await state.ReadHistoryCommand.ExecuteAsync();
                state.HistoryRows.Should().BeEmpty();
                fields[0].Focus();
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                fields.Should().OnlyContain(field => field.Text == "");
                state.HistoryRows.Should().BeEmpty();
                state.CanReadHistory.Should().BeTrue();
                state.CanRevoke.Should().BeFalse();
                state.NextHistoryCommand.CanExecute(null).Should().BeFalse();
                SHA256.HashData(File.ReadAllBytes(f.DatabasePath)).Should().Equal(before);
            }
            finally { window.Close(); }
            state.HistoryRows.Should().BeEmpty();
        });
    }
}
