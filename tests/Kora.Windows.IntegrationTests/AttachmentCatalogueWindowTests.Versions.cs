using System.Globalization;
using System.Text.Json.Nodes;

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
    private static readonly string[] InvalidVersionInputs =
        [" ", "0", "-1", "+1", "1 ", "1\n", "1,000", "1.0", "１", "9223372036854775808"];

    [Theory]
    [InlineData("AttachmentMinimumSessionGeneration")]
    [InlineData("AttachmentMaximumSessionGeneration")]
    [InlineData("AttachmentMinimumCapturedGeneration")]
    [InlineData("AttachmentMaximumCapturedGeneration")]
    [InlineData("AttachmentMinimumStorageRevision")]
    [InlineData("AttachmentMaximumStorageRevision")]
    public async Task VersionFieldsAreAccessibleStrictNoReadAndRetireDetailsContinuationsAndLatePages(string name)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var summary = Summary(HostRequest.Create(RequestOrigin.LocalUi));
            var seen = new List<SessionAttachmentQuery>();
            TaskCompletionSource<SessionAttachmentPage>? pending = null;
            CancellationToken observedToken = default;
            Func<bool>? current = null;
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            using var model = new AttachmentCatalogueViewModel((query, _, admitted, token) =>
            {
                seen.Add(query);
                observedToken = token;
                current = admitted;
                return pending?.Task ?? Task.FromResult(new SessionAttachmentPage(query, query.Matches(summary) ? [summary] : [],
                    "YQ==." + new string('b', 64), DateTimeOffset.UnixEpoch, 1, new string('b', 64), 1, false));
            }, (_, _, _) => throw new InvalidOperationException("No implicit navigation."), access, () => true, new CatalogueLogger());
            var revoked = 0;
            model.SelectionRevoked += _ => revoked++;
            var window = new AttachmentCatalogueWindow(model);
            window.Show();
            try
            {
                var field = window.FindControl<TextBox>(name)!;
                ControlAutomationPeer.CreatePeerForElement(field)!.GetName().Should().Contain("Optional inclusive")
                    .And.Contain("positive invariant digits").And.Contain("performs no read");
                field.Focus();
                field.IsFocused.Should().BeTrue();
                seen.Should().BeEmpty();
                await model.Refresh();
                model.Select(summary);
                field.Text = "1";
                model.Records.Should().BeEmpty();
                model.SelectedDetails.Should().BeEmpty();
                model.CanNavigate.Should().BeFalse();
                model.CanNext.Should().BeFalse();
                revoked.Should().Be(1);
                seen.Should().HaveCount(1);
                model.MinimumSessionGenerationText = summary.SessionGeneration.Value.ToString(CultureInfo.InvariantCulture);
                model.MaximumSessionGenerationText = model.MinimumSessionGenerationText;
                model.MinimumCapturedGenerationText = summary.CapturedGeneration.Value.ToString(CultureInfo.InvariantCulture);
                model.MaximumCapturedGenerationText = model.MinimumCapturedGenerationText;
                model.MinimumStorageRevisionText = summary.StorageRevision.Value.ToString(CultureInfo.InvariantCulture);
                model.MaximumStorageRevisionText = model.MinimumStorageRevisionText;
                model.DigestText = summary.File.Digest;
                model.ObservationSourceText = summary.ObservationSource.ToString("D");
                model.DeviceProfileText = summary.Profile.Value.ToString("D");
                model.CapturedFromText = summary.CapturedAt.ToString("O", CultureInfo.InvariantCulture);
                model.CapturedUntilText = model.CapturedFromText;
                model.Mode = AttachmentCatalogueMode.ExactId;
                model.QueryText = summary.Session.Value.ToString("D");
                model.MetadataText = "text/";
                model.Filter = SessionAttachmentFilter.Active;
                model.MediaType = summary.MediaType;
                model.CopyStatus = model.CopyStatuses.Single(choice => choice.Status == AttachmentCopyStatus.Confirmed);
                model.MinimumBytesText = summary.StoredBytes.ToString(CultureInfo.InvariantCulture);
                model.MaximumBytesText = model.MinimumBytesText;
                field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                await window.Operation;
                seen.Last().Should().Be(new SessionAttachmentQuery(false, model.QueryText, model.Filter, "text/", summary.MediaType,
                    AttachmentCopyStatus.Confirmed, summary.StoredBytes, summary.StoredBytes, summary.File.Digest,
                    model.ObservationSourceText, model.DeviceProfileText, summary.CapturedAt, summary.CapturedAt,
                    summary.SessionGeneration.Value, summary.SessionGeneration.Value,
                    summary.CapturedGeneration.Value, summary.CapturedGeneration.Value, summary.StorageRevision.Value, summary.StorageRevision.Value));
                model.Select(summary);
                var valid = field.Text!;
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
                foreach (var invalid in InvalidVersionInputs)
                {
                    field.Text = invalid;
                    var reads = seen.Count;
                    model.CanSearch.Should().BeFalse();
                    model.VersionHint.Should().Contain("Invalid");
                    field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                    await model.Refresh();
                    seen.Should().HaveCount(reads);
                    model.Status.Should().Contain("InvalidDataException");
                }
                model.ClearQuery();
                field.Text.Should().BeEmpty();
                model.MinimumStorageRevisionText = "9223372036854775807";
                await model.Refresh();
                model.Records.Should().BeEmpty();
                model.CanNavigate.Should().BeFalse();
                model.ClearQuery();
                foreach (var pair in new[] {
                    ("AttachmentMinimumSessionGeneration", "AttachmentMaximumSessionGeneration"),
                    ("AttachmentMinimumCapturedGeneration", "AttachmentMaximumCapturedGeneration"),
                    ("AttachmentMinimumStorageRevision", "AttachmentMaximumStorageRevision") })
                {
                    window.FindControl<TextBox>(pair.Item1)!.Text = "2";
                    window.FindControl<TextBox>(pair.Item2)!.Text = "1";
                    model.CanSearch.Should().BeFalse();
                    var reads = seen.Count;
                    await model.Refresh();
                    seen.Should().HaveCount(reads);
                    model.Status.Should().Contain("InvalidDataException");
                    model.ClearQuery();
                }
                field.Text = "1";
                model.Revoke();
                field.Text.Should().BeEmpty();
                field.Text = "1";
            }
            finally { window.Close(); }
            model.MinimumSessionGenerationText.Should().BeEmpty();
            model.MaximumSessionGenerationText.Should().BeEmpty();
            model.MinimumCapturedGenerationText.Should().BeEmpty();
            model.MaximumCapturedGenerationText.Should().BeEmpty();
            model.MinimumStorageRevisionText.Should().BeEmpty();
            model.MaximumStorageRevisionText.Should().BeEmpty();
            model.MinimumSessionGenerationText = "3";
            model.MinimumSessionGenerationText.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task TombstoneNativeRowsAndDetailsNeverExposeFormerVersions()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var row = Summary(HostRequest.Create(RequestOrigin.LocalUi)) with { CopyStatus = AttachmentCopyStatus.Unavailable,
                StoredBytes = 0, SessionGeneration = new(1234567), CapturedGeneration = new(1234566), StorageRevision = new(1234565) };
            using var model = new AttachmentCatalogueViewModel((query, _, _, _) => Task.FromResult(
                new SessionAttachmentPage(query, [row], null, DateTimeOffset.UnixEpoch, 1, new string('b', 64), 1, false)),
                (_, _, _) => throw new InvalidOperationException("Unavailable."),
                new WindowsSqliteSessionWorkspaceTests.Access(), () => true, new CatalogueLogger());
            model.IncludeRemoved = true;
            var window = new AttachmentCatalogueWindow(model);
            window.Show();
            try
            {
                await model.Refresh();
                model.Select(row);
                var details = JsonNode.Parse(model.SelectedDetails)!;
                details["SessionGeneration"].Should().BeNull();
                details["CapturedGeneration"].Should().BeNull();
                details["StorageRevision"].Should().BeNull();
                Text(window).Should().NotContain("1234567").And.NotContain("1234566").And.NotContain("1234565");
                model.CanNavigate.Should().BeFalse();
            }
            finally { window.Close(); }
        });
    }
}
