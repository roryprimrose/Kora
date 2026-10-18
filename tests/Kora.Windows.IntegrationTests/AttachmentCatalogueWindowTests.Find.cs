using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AttachmentCatalogueWindowTests
{
    [WindowsFact]
    public async Task NativeMetadataFindIsExplicitReadFreeAccessibleAndDoesNotChangePrimarySelection()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var summary = Summary(HostRequest.Create(RequestOrigin.LocalUi));
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var reads = 0;
            using var model = new AttachmentCatalogueViewModel((query, _, _, _) =>
            {
                reads++;
                return Task.FromResult(FindPage(query, summary));
            }, (_, _, _) => throw new InvalidOperationException("Find must never navigate."),
                access, () => true, NullLogger<AttachmentCatalogueViewModel>.Instance);
            var window = new AttachmentCatalogueWindow(model);
            window.Show();
            try
            {
                window.Width = window.MinWidth;
                window.Height = window.MinHeight;
                var query = window.FindControl<TextBox>("MetadataFindQuery")!;
                var first = window.FindControl<Button>("MetadataFindFirst")!;
                var next = window.FindControl<Button>("MetadataFindNext")!;
                var previous = window.FindControl<Button>("MetadataFindPrevious")!;
                var clear = window.FindControl<Button>("MetadataFindClear")!;
                var span = window.FindControl<TextBlock>("MetadataFindSpan")!;
                var outcome = window.FindControl<TextBlock>("MetadataFindStatus")!;
                query.IsEnabled.Should().BeFalse();
                model.FindMetadata(SnapshotFindDirection.First);
                model.MetadataFindStatus.Should().BeEmpty();
                reads.Should().Be(0);
                await model.Refresh();
                model.CanFindMetadata.Should().BeFalse();
                model.Select(summary);
                var snapshot = model.SelectedDetails;
                foreach (var control in new Control[] { query, first, next, previous, clear, span, outcome })
                {
                    ControlAutomationPeer.CreatePeerForElement(control)!.GetName().Should().NotBeNullOrEmpty();
                }
                query.IsUndoEnabled.Should().BeFalse();
                query.Focus();
                query.IsFocused.Should().BeTrue();
                query.Text = "Value";
                reads.Should().Be(1);
                next.IsEnabled.Should().BeFalse();
                first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                span.Text.Should().Be("Value");
                span.IsVisible.Should().BeTrue();
                span.IsFocused.Should().BeTrue();
                outcome.Text.Should().Contain("UTF-16");
                ControlAutomationPeer.CreatePeerForElement(span)!.GetName().Should().Contain("Value");
                ControlAutomationPeer.CreatePeerForElement(outcome)!.GetName().Should().Contain("UTF-16");
                window.UpdateLayout();
                var viewport = span.GetVisualAncestors().OfType<ScrollViewer>().First();
                viewport.Viewport.Height.Should().BeGreaterThan(0);
                var matchPoint = span.TranslatePoint(default, viewport)!.Value;
                matchPoint.Y.Should().BeGreaterThanOrEqualTo(0);
                (matchPoint.Y + span.Bounds.Height).Should().BeLessThanOrEqualTo(viewport.Bounds.Height);
                var firstStatus = model.MetadataFindStatus;
                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.MetadataFindStatus.Should().NotBe(firstStatus);
                previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.MetadataFindStatus.Should().Be(firstStatus);
                previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.MetadataFindStatus.Should().Contain("no wrap");
                span.IsVisible.Should().BeFalse();
                query.Text = "not in admitted metadata";
                span.Text.Should().BeEmpty();
                outcome.Text.Should().BeEmpty();
                ControlAutomationPeer.CreatePeerForElement(span)!.GetName().Should().NotContain("Value");
                first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                outcome.Text.Should().StartWith("No literal match");
                foreach (var invalid in new[] { "", "\n", "\u200b", new string('x', 257), new string('界', 171), new string('\ud800', 1) })
                {
                    model.MetadataFindQuery = invalid;
                    first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    model.MetadataFindStatus.Should().StartWith("Invalid");
                    span.IsVisible.Should().BeFalse();
                    next.IsEnabled.Should().BeFalse();
                }
                query.Text = "Session";
                query.Focus();
                query.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                model.HasMetadataFindMatch.Should().BeTrue();
                query.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                window.IsVisible.Should().BeTrue();
                model.MetadataFindQuery.Should().BeEmpty();
                outcome.Text.Should().BeEmpty();
                query.Text = "Session";
                first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                query.Text.Should().BeEmpty();
                span.Text.Should().BeEmpty();
                model.SelectedRecord.Should().BeSameAs(summary);
                model.SelectedDetails.Should().Be(snapshot);
                model.QueryText.Should().BeEmpty();
                model.MetadataText.Should().BeEmpty();
                model.CanNavigate.Should().BeTrue();
                reads.Should().Be(1);
                foreach (var transfer in new[] { TextBox.CopyingToClipboardEvent, TextBox.CuttingToClipboardEvent, TextBox.PastingFromClipboardEvent })
                {
                    var args = new RoutedEventArgs(transfer);
                    query.RaiseEvent(args);
                    args.Handled.Should().BeTrue();
                }
                query.Text = "Session";
                first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Close();
                query.Text = "stale private query";
                first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.MetadataFindQuery.Should().BeEmpty();
                span.Text.Should().BeEmpty();
                outcome.Text.Should().BeEmpty();
                reads.Should().Be(1);
            }
            finally { window.Close(); }
        });
    }

    [WindowsFact]
    public async Task NativeMetadataFindRevocationDuringBringIntoViewCannotFocusOrRediscloseMatch()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var summary = Summary(HostRequest.Create(RequestOrigin.LocalUi));
            var reads = 0;
            using var model = new AttachmentCatalogueViewModel((query, _, _, _) =>
            {
                reads++;
                return Task.FromResult(FindPage(query, summary));
            }, (_, _, _) => throw new InvalidOperationException("No navigation authority."),
                new WindowsSqliteSessionWorkspaceTests.Access(), () => true, NullLogger<AttachmentCatalogueViewModel>.Instance);
            var window = new AttachmentCatalogueWindow(model);
            window.Show();
            try
            {
                await model.Refresh();
                model.Select(summary);
                model.MetadataFindQuery = "Session";
                var revoked = false;
                window.AddHandler(Control.RequestBringIntoViewEvent, (_, _) =>
                {
                    revoked = true;
                    model.Revoke();
                });
                window.FindControl<Button>("MetadataFindFirst")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                revoked.Should().BeTrue();
                model.MetadataFindMatch.Should().BeEmpty();
                model.MetadataFindQuery.Should().BeEmpty();
                model.SelectedDetails.Should().BeEmpty();
                var span = window.FindControl<TextBlock>("MetadataFindSpan")!;
                span.Text.Should().BeEmpty();
                span.IsVisible.Should().BeFalse();
                reads.Should().Be(1);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("selection")]
    [InlineData("filter")]
    [InlineData("query")]
    [InlineData("page")]
    [InlineData("refresh")]
    [InlineData("cancel")]
    [InlineData("privacy")]
    [InlineData("admission")]
    [InlineData("control")]
    [InlineData("source")]
    [InlineData("dispose")]
    [InlineData("navigate")]
    public async Task NativeMetadataFindRetiresPrivateStringsAcrossEveryOwnedBoundary(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var summary = Summary(HostRequest.Create(RequestOrigin.LocalUi));
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var admitted = true;
            var reads = 0;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var model = new AttachmentCatalogueViewModel(async (query, _, _, _) =>
            {
                if (++reads > 1) { entered.SetResult(); await release.Task; }
                return FindPage(query, summary);
            }, async (_, _, _) => { entered.SetResult(); await release.Task; },
                access, () => admitted, NullLogger<AttachmentCatalogueViewModel>.Instance);
            var window = new AttachmentCatalogueWindow(model);
            window.Show();
            try
            {
                await model.Refresh();
                model.Select(summary);
                model.MetadataFindQuery = "Session";
                model.FindMetadata(SnapshotFindDirection.First);
                model.HasMetadataFindMatch.Should().BeTrue();
                Task? pending = null;
                switch (boundary)
                {
                    case "selection": model.Select(null); break;
                    case "filter": model.Filter = SessionAttachmentFilter.Done; break;
                    case "query": model.MetadataText = "text"; break;
                    case "page": pending = model.Refresh(next: true); break;
                    case "refresh": pending = model.Refresh(); break;
                    case "cancel": model.Cancel(); break;
                    case "privacy": access.CanInspect = false; break;
                    case "admission": admitted = false; break;
                    case "control": access.ControlRevision++; break;
                    case "source": model.Revoke(); break;
                    case "dispose": model.Dispose(); break;
                    case "navigate": pending = model.Navigate(); break;
                }
                if (pending is not null) { await entered.Task; }
                model.SynchronizeAuthority();
                model.MetadataFindQuery.Should().BeEmpty();
                model.MetadataFindMatch.Should().BeEmpty();
                model.MetadataFindStatus.Should().BeEmpty();
                model.CanFindMetadata.Should().BeFalse();
                model.FindMetadata(SnapshotFindDirection.Next);
                model.ClearMetadataFind();
                window.FindControl<Button>("MetadataFindFirst")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.FindControl<TextBlock>("MetadataFindSpan")!.Text.Should().BeEmpty();
                window.FindControl<TextBlock>("MetadataFindStatus")!.Text.Should().BeEmpty();
                if (pending is not null)
                {
                    model.Cancel();
                    release.SetResult();
                    await pending;
                    model.Records.Should().BeEmpty();
                    model.CanFindMetadata.Should().BeFalse();
                }
            }
            finally { release.TrySetResult(); window.Close(); }
        });
    }

    [WindowsFact]
    public async Task NativeMetadataFindCannotSearchSuppressedTombstoneBackingFieldsOrResurrectRemovedSelection()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var original = Summary(HostRequest.Create(RequestOrigin.LocalUi));
            var tombstone = original with { CopyStatus = AttachmentCopyStatus.Unavailable, StoredBytes = 0,
                SessionState = AttachmentSessionState.Removed };
            var row = original;
            using var model = new AttachmentCatalogueViewModel((query, _, _, _) => Task.FromResult(FindPage(query, row)),
                (_, _, _) => throw new InvalidOperationException("No body read."),
                new WindowsSqliteSessionWorkspaceTests.Access(), () => true, NullLogger<AttachmentCatalogueViewModel>.Instance);
            var window = new AttachmentCatalogueWindow(model);
            window.Show();
            try
            {
                model.IncludeRemoved = true;
                await model.Refresh();
                model.Select(original);
                model.MetadataFindQuery = original.Profile.Value.ToString("D");
                model.FindMetadata(SnapshotFindDirection.First);
                model.HasMetadataFindMatch.Should().BeTrue();
                model.Revoke();
                row = tombstone;
                await model.Refresh();
                model.Select(tombstone);
                var details = model.SelectedDetails;
                details.Should().NotContain("\"StoredBytes\"").And.NotContain(original.Profile.Value.ToString("D"))
                    .And.NotContain(original.ObservationSource.ToString("D")).And.NotContain(original.File.Digest);
                foreach (var query in new[] { "StoredBytes", original.Profile.Value.ToString("D"),
                    original.ObservationSource.ToString("D"), original.File.Digest })
                {
                    model.MetadataFindQuery = query;
                    model.FindMetadata(SnapshotFindDirection.First);
                    model.MetadataFindStatus.Should().StartWith("No literal match");
                }
                model.Select(original);
                model.SelectedRecord.Should().BeSameAs(tombstone);
                model.CanNavigate.Should().BeFalse();
                model.MetadataFindQuery = "Removed";
                model.FindMetadata(SnapshotFindDirection.First);
                // Enums retain the existing numeric JSON rendering; Find never invents labels.
                model.MetadataFindStatus.Should().StartWith("No literal match");
                model.MetadataFindQuery = "SessionState";
                model.FindMetadata(SnapshotFindDirection.First);
                model.MetadataFindMatch.Should().Be("SessionState");
            }
            finally { window.Close(); }
        });
    }

    private static SessionAttachmentPage FindPage(SessionAttachmentQuery query, SessionAttachmentSummary summary) =>
        new(query, [summary], "YQ==." + new string('a', 64), DateTimeOffset.UnixEpoch, 1, new string('a', 64), 1, false);
}
