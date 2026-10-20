using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AttachmentCatalogueWindowTests
{
    [WindowsFact]
    public async Task LoadedPageOrderIsAccessibleReadFreeRetiresSelectionAndFindAndPreservesSemanticNext()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var row = Summary(HostRequest.Create(RequestOrigin.LocalUi));
            var other = row with { Session = new(Guid.NewGuid()), StoredBytes = 0 };
            var reads = new List<(SessionAttachmentQuery Query, string? Cursor)>();
            const string cursor = "YQ==.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            using var model = new AttachmentCatalogueViewModel((query, next, _, _) =>
            {
                reads.Add((query, next));
                return Task.FromResult(new SessionAttachmentPage(query, [row, other], next is null ? cursor : null,
                    DateTimeOffset.UnixEpoch, 1, new string('a', 64), 64, next is null));
            }, (_, _, _) => throw new InvalidOperationException("Order never navigates."),
                new WindowsSqliteSessionWorkspaceTests.Access(), () => true, NullLogger<AttachmentCatalogueViewModel>.Instance);
            var window = new AttachmentCatalogueWindow(model);
            window.Show();
            try
            {
                var picker = window.FindControl<ComboBox>("AttachmentPageOrder")!;
                var clear = window.FindControl<Button>("ClearAttachmentPageOrder")!;
                picker.IsEnabled.Should().BeFalse();
                model.PageOrder!.Value.Should().Be(SessionAttachmentPageOrder.Source);
                model.PageOrders.Should().HaveCount(5);
                ControlAutomationPeer.CreatePeerForElement(picker)!.GetName()
                    .Should().Contain("Order this loaded page").And.Contain("metadata only").And.Contain("no read");
                ControlAutomationPeer.CreatePeerForElement(clear)!.GetName().Should().Contain("Source order");
                var disclosure = window.FindControl<TextBlock>("AttachmentPageOrderDisclosure")!;
                ControlAutomationPeer.CreatePeerForElement(disclosure)!.GetName().Should().Contain("Next keeps the original");
                await model.Refresh();
                picker.Focus();
                picker.IsFocused.Should().BeTrue();
                model.Records.Should().Equal(row, other);
                var revoked = 0;
                model.SelectionRevoked += _ => revoked++;
                foreach (var choice in model.PageOrders.Skip(1))
                {
                    var stale = model.Records[0];
                    model.Select(stale);
                    model.MetadataFindQuery = "Session";
                    model.FindMetadata(Kora.Core.Context.SnapshotFindDirection.First);
                    model.HasMetadataFindMatch.Should().BeTrue();
                    picker.SelectedItem = choice;
                    model.PageOrder.Should().Be(choice);
                    model.SelectedRecord.Should().BeNull();
                    model.SelectedDetails.Should().BeEmpty();
                    model.MetadataFindQuery.Should().BeEmpty();
                    model.MetadataFindMatch.Should().BeEmpty();
                    model.CanNavigate.Should().BeFalse();
                    model.Select(stale);
                    model.SelectedRecord.Should().BeNull("retired list callbacks have no current presentation identity");
                    model.CanNext.Should().BeTrue();
                    reads.Should().ContainSingle();
                }
                revoked.Should().Be(4);
                clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.PageOrder!.Value.Should().Be(SessionAttachmentPageOrder.Source);
                model.Records.Should().Equal(row, other);
                reads.Should().ContainSingle();
                await model.Refresh(next: true);
                reads.Should().HaveCount(2);
                reads[1].Should().Be((reads[0].Query, cursor));
                model.CanNext.Should().BeFalse();
                model.PageOrder = null;
                model.Records.Should().BeEmpty();
                model.CanSearch.Should().BeFalse();
                model.Status.Should().Contain("Invalid loaded-page order");
                clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.Records.Should().Equal(row, other);
                model.CanSearch.Should().BeTrue();
                reads.Should().HaveCount(2);
            }
            finally { window.Close(); }
        });
    }

    [WindowsFact]
    public async Task OrderEditCancelsInFlightOpenAndLateCompletionCannotClearReorderedPage()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var row = Summary(HostRequest.Create(RequestOrigin.LocalUi));
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<bool>? original = null;
            CancellationToken captured = default;
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var admitted = true;
            using var model = new AttachmentCatalogueViewModel((query, _, _, _) => Task.FromResult(FindPage(query, row)),
                (_, current, token) => { original = current; captured = token; return completed.Task; },
                access, () => admitted, NullLogger<AttachmentCatalogueViewModel>.Instance);
            await model.Refresh();
            model.Select(row);
            var navigation = model.Navigate();
            model.PageOrder = model.PageOrders[3];
            captured.IsCancellationRequested.Should().BeTrue();
            original!().Should().BeFalse();
            completed.SetResult();
            await navigation;
            model.Records.Should().ContainSingle();
            model.CanNavigate.Should().BeFalse();
            model.Status.Should().Contain("without a read");
            model.Select(model.Records[0]);
            model.MetadataFindQuery = "Session";
            admitted = false;
            model.PageOrder = model.PageOrders[4];
            model.Records.Should().BeEmpty();
            model.SelectedDetails.Should().BeEmpty();
            model.MetadataFindQuery.Should().BeEmpty();
            model.PageOrder!.Value.Should().Be(SessionAttachmentPageOrder.Source);
            model.Dispose();
            model.PageOrder = model.PageOrders[3];
            model.Records.Should().BeEmpty();
        });
    }

    [WindowsFact]
    public async Task MalformedEntirePageNeverBecomesOrderable()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var row = Summary(HostRequest.Create(RequestOrigin.LocalUi));
            using var model = new AttachmentCatalogueViewModel((query, _, _, _) =>
                Task.FromResult(FindPage(query, row with { StoredBytes = -1 })),
                (_, _, _) => throw new InvalidOperationException("No navigation."),
                new WindowsSqliteSessionWorkspaceTests.Access(), () => true, NullLogger<AttachmentCatalogueViewModel>.Instance);
            await model.Refresh();
            model.Status.Should().Contain("InvalidDataException");
            model.CanOrderPage.Should().BeFalse();
            model.PageOrder = model.PageOrders[3];
            model.Records.Should().BeEmpty();
            model.PageOrder!.Value.Should().Be(SessionAttachmentPageOrder.Source);
        });
    }
}
