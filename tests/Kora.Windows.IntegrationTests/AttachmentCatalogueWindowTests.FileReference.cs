using System.Globalization;

using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;

using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AttachmentCatalogueWindowTests
{
    [Theory]
    [InlineData("AttachmentFileSourceId")]
    [InlineData("AttachmentFileRevisionId")]
    [InlineData("AttachmentFileItemId")]
    public async Task FileReferenceFieldsAreAccessibleStrictExplicitAndRetireDetailsContinuationAndLatePages(string name)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var summary = Summary(HostRequest.Create(RequestOrigin.LocalUi));
            var seen = new List<SessionAttachmentQuery>();
            TaskCompletionSource<SessionAttachmentPage>? pending = null;
            CancellationToken observedToken = default;
            Func<bool>? current = null;
            using var model = new AttachmentCatalogueViewModel((query, _, admitted, token) =>
            {
                seen.Add(query);
                observedToken = token;
                current = admitted;
                return pending?.Task ?? Task.FromResult(new SessionAttachmentPage(query, query.Matches(summary) ? [summary] : [],
                    "YQ==." + new string('b', 64), DateTimeOffset.UnixEpoch, 1, new string('b', 64), 1, false));
            }, (_, _, _) => throw new InvalidOperationException("No implicit navigation."),
                new WindowsSqliteSessionWorkspaceTests.Access(), () => true, new CatalogueLogger());
            var revoked = 0;
            model.SelectionRevoked += _ => revoked++;
            var window = new AttachmentCatalogueWindow(model);
            window.Show();
            try
            {
                var field = window.FindControl<TextBox>(name)!;
                ControlAutomationPeer.CreatePeerForElement(field)!.GetName().Should().Contain("Optional exact retained local-file")
                    .And.Contain("nonempty canonical lowercase D-format GUID").And.Contain("correlation only").And.Contain("performs no read");
                field.Focus();
                field.IsFocused.Should().BeTrue();
                seen.Should().BeEmpty();
                await model.Refresh();
                model.Select(summary);
                var valid = name switch
                {
                    "AttachmentFileSourceId" => summary.File.SourceId.ToString("D"),
                    "AttachmentFileRevisionId" => summary.File.RevisionId.ToString("D"),
                    _ => summary.File.ItemId.ToString("D")
                };
                field.Text = valid;
                model.Records.Should().BeEmpty();
                model.SelectedDetails.Should().BeEmpty();
                model.CanNavigate.Should().BeFalse();
                model.CanNext.Should().BeFalse();
                revoked.Should().Be(1);
                seen.Should().HaveCount(1);
                model.FileSourceIdText = summary.File.SourceId.ToString("D");
                model.FileRevisionIdText = summary.File.RevisionId.ToString("D");
                model.FileItemIdText = summary.File.ItemId.ToString("D");
                model.Mode = AttachmentCatalogueMode.ExactId;
                model.QueryText = summary.Session.Value.ToString("D");
                model.Filter = SessionAttachmentFilter.Active;
                model.MetadataText = "text/";
                model.MediaType = summary.MediaType;
                model.CopyStatus = model.CopyStatuses.Single(choice => choice.Status == AttachmentCopyStatus.Confirmed);
                model.MinimumBytesText = summary.StoredBytes.ToString(CultureInfo.InvariantCulture);
                model.MaximumBytesText = model.MinimumBytesText;
                model.DigestText = summary.File.Digest;
                model.ObservationSourceText = summary.ObservationSource.ToString("D");
                model.DeviceProfileText = summary.Profile.Value.ToString("D");
                model.CapturedFromText = summary.CapturedAt.ToString("O", CultureInfo.InvariantCulture);
                model.CapturedUntilText = model.CapturedFromText;
                model.MinimumSessionGenerationText = summary.SessionGeneration.Value.ToString(CultureInfo.InvariantCulture);
                model.MaximumSessionGenerationText = model.MinimumSessionGenerationText;
                model.MinimumCapturedGenerationText = summary.CapturedGeneration.Value.ToString(CultureInfo.InvariantCulture);
                model.MaximumCapturedGenerationText = model.MinimumCapturedGenerationText;
                model.MinimumStorageRevisionText = summary.StorageRevision.Value.ToString(CultureInfo.InvariantCulture);
                model.MaximumStorageRevisionText = model.MinimumStorageRevisionText;
                field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                await window.Operation;
                seen.Last().Should().Be(new SessionAttachmentQuery(false, model.QueryText, model.Filter, "text/", summary.MediaType,
                    AttachmentCopyStatus.Confirmed, summary.StoredBytes, summary.StoredBytes, summary.File.Digest,
                    model.ObservationSourceText, model.DeviceProfileText, summary.CapturedAt, summary.CapturedAt,
                    summary.SessionGeneration.Value, summary.SessionGeneration.Value,
                    summary.CapturedGeneration.Value, summary.CapturedGeneration.Value, summary.StorageRevision.Value, summary.StorageRevision.Value,
                    model.FileSourceIdText, model.FileRevisionIdText, model.FileItemIdText));
                model.Select(summary);
                field.Text = valid;
                revoked.Should().Be(1);
                await model.Refresh(next: true);
                model.Select(summary);
                pending = new();
                var work = model.Refresh();
                var original = seen.Last();
                field.Text = " ";
                observedToken.IsCancellationRequested.Should().BeTrue();
                current!().Should().BeFalse();
                pending.SetResult(new(original, [summary], null, DateTimeOffset.UnixEpoch, 1, new string('b', 64), 1, false));
                await work;
                pending = null;
                model.Records.Should().BeEmpty();
                model.SelectedDetails.Should().BeEmpty();
                foreach (var invalid in new[] { " ", valid + " ", valid + "\n", "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA",
                    Guid.Empty.ToString("D"), Guid.NewGuid().ToString("N"), "{" + valid + "}", "not a GUID" })
                {
                    field.Text = invalid;
                    var reads = seen.Count;
                    model.CanSearch.Should().BeFalse();
                    model.FileReferenceHint.Should().Contain("Invalid");
                    field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                    await model.Refresh();
                    seen.Should().HaveCount(reads);
                    model.Status.Should().Contain("InvalidDataException");
                }
                model.ClearQuery();
                field.Text.Should().BeEmpty();
                model.CanSearch.Should().BeTrue();
                field.Text = Guid.NewGuid().ToString("D");
                await model.Refresh();
                model.Records.Should().BeEmpty();
                model.Status.Should().Contain("No matching retained metadata");
                field.Text = valid;
                pending = new();
                work = model.Refresh();
                original = seen.Last();
                model.Cancel();
                observedToken.IsCancellationRequested.Should().BeTrue();
                field.Text.Should().BeEmpty();
                pending.SetResult(new(original, [], null, DateTimeOffset.UnixEpoch, 1, new string('b', 64), 1, false));
                await work;
                pending = null;
                model.Records.Should().BeEmpty();
                field.Text = valid;
                model.Revoke();
                field.Text.Should().BeEmpty();
                field.Text = valid;
            }
            finally { window.Close(); }
            model.FileSourceIdText.Should().BeEmpty();
            model.FileRevisionIdText.Should().BeEmpty();
            model.FileItemIdText.Should().BeEmpty();
            model.FileSourceIdText = "changed after close";
            model.FileSourceIdText.Should().BeEmpty();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrivateOrControlRevocationClearsFileIdsAndCancelsExactNavigation(bool privateClosure)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var summary = Summary(HostRequest.Create(RequestOrigin.LocalUi));
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var pending = new TaskCompletionSource();
            CancellationToken navigationToken = default;
            Func<bool>? current = null;
            using var model = new AttachmentCatalogueViewModel((query, _, _, _) => Task.FromResult(
                new SessionAttachmentPage(query, [summary], null, DateTimeOffset.UnixEpoch, 1, new string('b', 64), 1, false)),
                (_, admitted, token) =>
                {
                    current = admitted;
                    navigationToken = token;
                    return pending.Task;
                }, access, () => true, new CatalogueLogger());
            model.FileSourceIdText = summary.File.SourceId.ToString("D");
            model.FileRevisionIdText = summary.File.RevisionId.ToString("D");
            model.FileItemIdText = summary.File.ItemId.ToString("D");
            await model.Refresh();
            model.Select(summary);
            var navigation = model.Navigate();
            if (privateClosure) { access.CanInspect = false; }
            else { access.ControlRevision++; }
            model.SynchronizeAuthority();
            navigationToken.IsCancellationRequested.Should().BeTrue();
            current!().Should().BeFalse();
            model.FileSourceIdText.Should().BeEmpty();
            model.FileRevisionIdText.Should().BeEmpty();
            model.FileItemIdText.Should().BeEmpty();
            model.Records.Should().BeEmpty();
            model.SelectedDetails.Should().BeEmpty();
            model.CanNavigate.Should().BeFalse();
            pending.SetResult();
            await navigation;
            model.Status.Should().NotContain("Opened");
            model.SelectedDetails.Should().BeEmpty();
        });
    }
}
