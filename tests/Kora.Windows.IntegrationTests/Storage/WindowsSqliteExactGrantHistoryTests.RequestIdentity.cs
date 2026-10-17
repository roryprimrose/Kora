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
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteExactGrantHistoryTests
{
    private static readonly string[] RequestIdentityControlNames =
    [
        "Exact grant history committed request session ID",
        "Exact grant history committed request ID",
        "Exact grant history committed request task ID",
    ];

    [WindowsFact]
    public async Task RequestIdentityMatchesOnlyCommittedEnvelopesAcrossOriginalAndNativeControlSessionsWithoutWrites()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grants = await Grants(f, 2);
        await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grants[0].Id, grants[0].Revision, f.Token));
        await using var control = Control(f);
        var batch = await f.Store.PreviewExactGrantBatchAsync(
            new(Guid.NewGuid(), (await f.Store.ReadExactGrantPageAsync(null, f.Token)).Records), f.Token);
        await control.RevokeBatchAsync(batch, static () => true, f.Token);
        var exact = (await f.Store.InspectExactGrantAsync(grants[0].Id, f.Token))!;
        var viewer = HostRequest.Create(RequestOrigin.LocalUi);
        var before = SHA256.HashData(File.ReadAllBytes(f.DatabasePath));
        var all = await ReadFiltered(f, exact, ExactGrantHistoryQuery.Unfiltered, viewer);
        all.Records.Should().HaveCount(3);
        all.Records.Select(row => row.Request.SessionId).Distinct().Should().HaveCount(2);
        var original = grants[0].ApprovedProposal.Request;
        var consume = all.Records.Single(row => string.Equals(row.Audit.ActionId, "approval.consume", StringComparison.Ordinal));
        consume.Request.SessionId.Should().Be(original.SessionId);
        consume.Request.RequestId.Should().NotBe(original.RequestId);
        consume.Request.TaskId.Should().NotBe(original.TaskId);
        var revoked = all.Records[^1];
        revoked.Request.SessionId.Should().NotBe(original.SessionId);
        revoked.Provenance.RequestedGrants.Should().ContainSingle();
        revoked.OtherChanges.Should().Be(1);
        foreach (var row in all.Records)
        {
            var request = row.Request;
            foreach (var query in new[]
            {
                new ExactGrantHistoryQuery(sessionId: request.SessionId.Value),
                new ExactGrantHistoryQuery(requestId: request.RequestId.Value),
                new ExactGrantHistoryQuery(taskId: request.TaskId.Value),
                new ExactGrantHistoryQuery(row.Audit.Outcome, row.Audit.ReasonCode, row.Audit.Initiator,
                    row.Retention, row.CommittedAt, row.CommittedAt, row.DueAt, row.DueAt, row.Audit.CorrelationId,
                    row.Audit.ApprovalId is null ? ExactGrantHistoryApproval.Absent : ExactGrantHistoryApproval.Exact,
                    row.Audit.ApprovalId, request.SessionId.Value, request.RequestId.Value, request.TaskId.Value),
            })
            {
                var page = await ReadFiltered(f, exact, query, viewer);
                page.Records.Select(entry => entry.Provenance.Sequence).Should()
                    .Equal(all.Records.Where(entry =>
                        (query.SessionId is null || query.SessionId == entry.Request.SessionId.Value)
                        && (query.RequestId is null || query.RequestId == entry.Request.RequestId.Value)
                        && (query.TaskId is null || query.TaskId == entry.Request.TaskId.Value)
                        && (query.AuditCorrelationId is null || query.AuditCorrelationId == entry.Audit.CorrelationId))
                        .Select(entry => entry.Provenance.Sequence));
                page.Current.Should().Be(exact);
                page.NonmatchingRows.Should().Be(page.ScannedRows - page.Records.Length);
                page.AuditCeilingDigest.Should().Be(all.AuditCeilingDigest);
            }
        }
        foreach (var query in new[]
        {
            new ExactGrantHistoryQuery(sessionId: viewer.SessionId.Value),
            new ExactGrantHistoryQuery(requestId: viewer.RequestId.Value),
            new ExactGrantHistoryQuery(taskId: viewer.TaskId.Value),
            new ExactGrantHistoryQuery(sessionId: revoked.Request.SessionId.Value, requestId: original.RequestId.Value),
            new ExactGrantHistoryQuery(sessionId: original.SessionId.Value, taskId: revoked.Request.TaskId.Value),
            new ExactGrantHistoryQuery(requestId: grants[0].Id.Value),
            new ExactGrantHistoryQuery(taskId: Guid.NewGuid()),
        })
        {
            (await ReadFiltered(f, exact, query, viewer)).Records.Should().BeEmpty();
        }
        var other = (await f.Store.InspectExactGrantAsync(grants[1].Id, f.Token))!;
        (await ReadFiltered(f, other, new(requestId: original.RequestId.Value), viewer)).Records.Should().BeEmpty();
        var nativeOnly = await ReadFiltered(f, exact, new(sessionId: revoked.Request.SessionId.Value), viewer);
        nativeOnly.Records.Should().ContainSingle().Which.Provenance.Sequence.Should().Be(revoked.Provenance.Sequence);
        SHA256.HashData(File.ReadAllBytes(f.DatabasePath)).Should().Equal(before);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        var reopened = (await f.Store.InspectExactGrantAsync(grants[0].Id, f.Token))!;
        (await ReadFiltered(f, reopened, new(requestId: revoked.Request.RequestId.Value), viewer))
            .Records.Should().ContainSingle().Which.Provenance.CommitDigest.Should().Be(revoked.Provenance.CommitDigest);
    }

    [WindowsFact]
    public async Task ZeroMatchRequestCursorAuthenticatesCompleteSemanticQueryAndAdvancesFiniteBoundedPrefix()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        AppendUnrelated(f, 4200);
        var grant = await f.GrantAsync("perpetual");
        var exact = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        var viewer = HostRequest.Create(RequestOrigin.LocalUi);
        var request = grant.ApprovedProposal.Request;
        var row = (await ReadFiltered(f, exact, ExactGrantHistoryQuery.Unfiltered, viewer)).Records;
        row.Should().BeEmpty();
        static ExactGrantHistoryQuery Query(Guid? session, Guid? intent, Guid? task) =>
            new(sessionId: session, requestId: intent, taskId: task);
        var query = Query(request.SessionId.Value, request.RequestId.Value, request.TaskId.Value);
        var before = SHA256.HashData(File.ReadAllBytes(f.DatabasePath));
        var first = await ReadFiltered(f, exact, query, viewer);
        first.Records.Should().BeEmpty();
        first.ScannedRows.Should().Be(4096);
        first.NonmatchingRows.Should().Be(4096);
        first.ScanLimitReached.Should().BeTrue();
        first.Next.Should().NotBeNull();
        var parts = first.Next!.Split('.');
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));
        payload.Should().Contain(query.Digest).And.NotContain(request.SessionId.Value.ToString("D"))
            .And.NotContain(request.RequestId.Value.ToString("D")).And.NotContain(request.TaskId.Value.ToString("D"));
        foreach (var changed in new[]
        {
            Query(null, request.RequestId.Value, request.TaskId.Value),
            Query(request.SessionId.Value, null, request.TaskId.Value),
            Query(request.SessionId.Value, request.RequestId.Value, null),
            Query(Guid.NewGuid(), request.RequestId.Value, request.TaskId.Value),
            Query(request.SessionId.Value, Guid.NewGuid(), request.TaskId.Value),
            Query(request.SessionId.Value, request.RequestId.Value, Guid.NewGuid()),
            Query(request.RequestId.Value, request.SessionId.Value, request.TaskId.Value),
            new ExactGrantHistoryQuery(sessionId: request.SessionId.Value, requestId: request.RequestId.Value,
                taskId: request.TaskId.Value, approval: ExactGrantHistoryApproval.Absent),
        })
        {
            var changedRead = () => ReadFiltered(f, exact, changed, viewer, first.Next);
            await changedRead.Should().ThrowAsync<InvalidDataException>();
        }
        var tampered = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload.Replace(query.Digest,
            ExactGrantHistoryQuery.Unfiltered.Digest, StringComparison.Ordinal))) + "." + parts[1];
        var tamperedRead = () => ReadFiltered(f, exact, ExactGrantHistoryQuery.Unfiltered, viewer, tampered);
        await tamperedRead.Should().ThrowAsync<InvalidDataException>();
        var second = await ReadFiltered(f, exact, query, viewer, first.Next);
        second.Records.Should().ContainSingle().Which.Request.Should().Be(request);
        second.ScannedThrough.Should().Be(first.AuditCeiling);
        second.AuditCeilingDigest.Should().Be(first.AuditCeilingDigest);
        second.Next.Should().BeNull();
        second.ScannedRows.Should().BeLessThanOrEqualTo(ExactGrantHistoryPage.MaximumScannedRows);
        ExactGrantHistoryPage.Serialize(second).Length.Should().BeLessThanOrEqualTo(ExactGrantHistoryPage.MaximumBytes);
        ExactGrantHistoryPage.Serialize(await ReadFiltered(f, exact, query, viewer, first.Next))
            .Should().Equal(ExactGrantHistoryPage.Serialize(second));
        SHA256.HashData(File.ReadAllBytes(f.DatabasePath)).Should().Equal(before);
    }

    [WindowsFact]
    public async Task RequestFilteredRetainedPerpetualHistoryDoesNotRequireOrSelectRemovedOriginalSession()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        var original = grant.ApprovedProposal.Request;
        foreach (var task in (await f.Store.ReadTaskPageAsync(f.Request.SessionId, null, 50, f.Token)).Records)
        {
            using var activity = HostActivity.BeginRoot(task.Request, HostActivityLayer.Application, HostOperation.Request);
            await f.Tasks.CommitAsync(task.Next(HostTaskState.Succeeded), task.Revision.Value, f.Token);
        }
        var disposition = await f.Store.PreviewDispositionAsync(f.Request.SessionId, new(1), 0, f.Token);
        f.Request = InteractionStorageFixture.NewRequest(f.Request.SessionId);
        await f.RunAsync(async () =>
        {
            await f.Store.RecordControlIntentAsync(f.Request, f.Token);
            await f.Store.DisposeSessionAsync(f.Request, disposition, static () => true, f.Token);
        });
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        var exact = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        exact.OriginSessionRemoved.Should().BeTrue();
        var before = SHA256.HashData(File.ReadAllBytes(f.DatabasePath));
        var viewer = HostRequest.Create(RequestOrigin.LocalUi);
        var page = await ReadFiltered(f, exact, new(sessionId: original.SessionId.Value,
            requestId: original.RequestId.Value, taskId: original.TaskId.Value), viewer);
        page.Records.Should().ContainSingle().Which.Request.Should().Be(original);
        viewer.SessionId.Should().NotBe(original.SessionId);
        SHA256.HashData(File.ReadAllBytes(f.DatabasePath)).Should().Equal(before);
    }

    [Theory]
    [InlineData("UPDATE security_audit_events SET envelope=json_remove(envelope,'$.Request.SessionId') WHERE sequence=1;")]
    [InlineData("UPDATE security_audit_events SET envelope=json_remove(envelope,'$.Request.RequestId') WHERE sequence=1;")]
    [InlineData("UPDATE security_audit_events SET envelope=json_remove(envelope,'$.Request.TaskId') WHERE sequence=1;")]
    [InlineData("UPDATE security_audit_events SET envelope=json_set(envelope,'$.Request',NULL) WHERE sequence=1;")]
    [InlineData("UPDATE security_audit_events SET envelope=json_set(envelope,'$.Request.SessionId.Value','00000000-0000-0000-0000-000000000000') WHERE sequence=1;")]
    [InlineData("UPDATE security_audit_events SET envelope=json_set(envelope,'$.Request.RequestId.Value','00000000-0000-0000-0000-000000000000') WHERE sequence=1;")]
    [InlineData("UPDATE security_audit_events SET envelope=json_set(envelope,'$.Request.TaskId.Value','00000000-0000-0000-0000-000000000000') WHERE sequence=1;")]
    [InlineData("UPDATE security_audit_events SET envelope=json_set(envelope,'$.SessionGeneration',0) WHERE sequence=1;")]
    [InlineData("UPDATE security_audit_events SET envelope=json_set(envelope,'$.Audit.TargetId','12345678-1234-1234-1234-123456789abc') WHERE sequence=1;")]
    public async Task ExcludedRequestMetadataCannotHideMissingEnvelopeIdentityOrInvalidProvenance(string corruption)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        var exact = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        f.Mutate(corruption);
        RehashCorrelationFixture(f);
        var read = () => ReadFiltered(f, exact, new(sessionId: Guid.NewGuid(), requestId: Guid.NewGuid(), taskId: Guid.NewGuid()));
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RequestFilteredScanCancellationAdmissionLossAndTimeoutReleaseResourcesWithoutWrites(int failure)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        var clock = new DeadlineClock(f.Time.Now);
        var store = new WindowsSqliteHostInteractionStore(f.Paths, f.Tasks, clock);
        await store.InitializeAsync(f.Token);
        var exact = (await store.InspectExactGrantAsync(grant.Id, f.Token))!;
        var viewer = HostRequest.Create(RequestOrigin.LocalUi);
        var before = SHA256.HashData(File.ReadAllBytes(f.DatabasePath));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        using var activity = HostActivity.BeginRoot(viewer, HostActivityLayer.Application, HostOperation.Evidence);
        var calls = 0;
        var read = () => store.ReadExactGrantHistoryAsync(exact, viewer,
            new(sessionId: Guid.NewGuid(), requestId: Guid.NewGuid(), taskId: Guid.NewGuid()),
            1, Guid.NewGuid(), null, () =>
            {
                if (++calls != 3) { return true; }
                if (failure == 0) { cancellation.Cancel(); }
                else if (failure == 1) { return false; }
                else { clock.Timer!.Fire(); }
                return true;
            }, cancellation.Token).AsTask();
        if (failure == 1) { await read.Should().ThrowAsync<InvalidOperationException>(); }
        else { await read.Should().ThrowAsync<OperationCanceledException>(); }
        clock.DueTime.Should().Be(TimeSpan.FromSeconds(5));
        clock.Timer!.Disposed.Should().BeTrue();
        SHA256.HashData(File.ReadAllBytes(f.DatabasePath)).Should().Equal(before);
        using var exclusive = new FileStream(f.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.None);
        exclusive.Length.Should().BeGreaterThan(0);
    }

    [WindowsFact]
    public async Task NativeRequestIdentityControlsSupportKeyboardEditValidationAndClearWithoutAuthorityOrReadOnEdit()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
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
                var inputs = RequestIdentityControlNames.Select(name => window.GetVisualDescendants().OfType<TextBox>().Single(item =>
                    AutomationProperties.GetName(item)?.StartsWith(name, StringComparison.Ordinal) == true)).ToArray();
                foreach (var input in inputs)
                {
                    input.Text.Should().BeEmpty();
                    input.Focusable.Should().BeTrue();
                    AutomationProperties.GetName(input).Should().Contain("passive correlation only").And.Contain("typing performs no read");
                }
                inputs[0].Focus();
                inputs[0].IsFocused.Should().BeTrue();
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                inputs[1].IsFocused.Should().BeTrue();
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                inputs[2].IsFocused.Should().BeTrue();
                window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
                inputs[1].IsFocused.Should().BeTrue();
                var before = SHA256.HashData(File.ReadAllBytes(f.DatabasePath));
                // An exclusive private-file handle makes any accidental storage read on edit fail deterministically.
                using (var exclusive = new FileStream(f.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    inputs[0].Text = grant.ApprovedProposal.Request.SessionId.Value.ToString("D");
                    inputs[1].Text = grant.ApprovedProposal.Request.RequestId.Value.ToString("D");
                    inputs[2].Text = grant.ApprovedProposal.Request.TaskId.Value.ToString("D");
                    state.HistoryRows.Should().BeEmpty();
                    state.NextHistoryCommand.CanExecute(null).Should().BeFalse();
                    state.CanReadHistory.Should().BeTrue();
                    state.CanRevoke.Should().BeFalse();
                    exclusive.Length.Should().BeGreaterThan(0);
                }
                state.HistorySessionId.Should().Be(inputs[0].Text);
                state.HistoryRequestId.Should().Be(inputs[1].Text);
                state.HistoryTaskId.Should().Be(inputs[2].Text);
                await state.ReadHistoryCommand.ExecuteAsync();
                state.HistoryRows.Should().ContainSingle();
                foreach (var input in inputs)
                {
                    input.Text = "12345678-1234-1234-1234-123456789ABC";
                    state.HistoryRows.Should().BeEmpty();
                    state.ReadHistoryCommand.Execute(null);
                    state.Message.Should().Contain("unavailable").And.Contain(nameof(InvalidDataException));
                    state.CanReadHistory.Should().BeFalse();
                    await state.ClearHistoryFiltersCommand.ExecuteAsync();
                    inputs.Should().OnlyContain(item => item.Text == string.Empty);
                    await state.RefreshAsync(f.Token);
                    state.Selected = state.Records[0];
                    await state.InspectAsync(f.Token);
                }
                SHA256.HashData(File.ReadAllBytes(f.DatabasePath)).Should().Equal(before);
            }
            finally { window.Close(); }
        });
    }
}
