using System.Globalization;

using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;

using AwesomeAssertions;

using Kora.Core.Storage;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AttachmentCatalogueWindowTests
{
    [Theory]
    [InlineData("AttachmentObservationSource")]
    [InlineData("AttachmentDeviceProfile")]
    [InlineData("AttachmentCapturedFrom")]
    [InlineData("AttachmentCapturedUntil")]
    public async Task CorrelationFieldsAreAccessibleStrictExplicitAndRetireSelectionAndLatePages(string name)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            var summary = Summary(fixture.Request);
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
                ControlAutomationPeer.CreatePeerForElement(field)!.GetName().Should().Contain("Optional").And.Contain("performs no read");
                field.Focus();
                field.IsFocused.Should().BeTrue();
                seen.Should().BeEmpty();
                await model.Refresh();
                model.Select(summary);
                var valid = name switch
                {
                    "AttachmentObservationSource" => summary.ObservationSource.ToString("D"),
                    "AttachmentDeviceProfile" => summary.Profile.Value.ToString("D"),
                    _ => summary.CapturedAt.ToString("O", CultureInfo.InvariantCulture)
                };
                field.Text = valid;
                model.Records.Should().BeEmpty();
                model.SelectedDetails.Should().BeEmpty();
                model.CanNavigate.Should().BeFalse();
                model.CanNext.Should().BeFalse();
                revoked.Should().Be(1);
                seen.Should().HaveCount(1);
                model.ObservationSourceText = summary.ObservationSource.ToString("D");
                model.DeviceProfileText = summary.Profile.Value.ToString("D");
                model.CapturedFromText = summary.CapturedAt.ToString("O", CultureInfo.InvariantCulture);
                model.CapturedUntilText = model.CapturedFromText;
                model.DigestText = summary.File.Digest;
                field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                await window.Operation;
                seen.Last().Should().Be(new SessionAttachmentQuery(exactDigest: summary.File.Digest,
                    exactObservationSource: model.ObservationSourceText, exactDeviceProfile: model.DeviceProfileText,
                    capturedFromUtc: summary.CapturedAt, capturedUntilUtc: summary.CapturedAt));
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
                foreach (var invalid in new[] { " ", valid + " ", valid + "\n", "00000000-0000-0000-0000-000000000000", "not canonical" })
                {
                    field.Text = invalid;
                    var reads = seen.Count;
                    model.CanSearch.Should().BeFalse();
                    model.CorrelationHint.Should().Contain("Invalid");
                    field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                    await model.Refresh();
                    seen.Should().HaveCount(reads);
                    model.Status.Should().Contain("InvalidDataException");
                }
                model.ClearQuery();
                field.Text.Should().BeEmpty();
                model.CanSearch.Should().BeTrue();
                model.CapturedFromText = summary.CapturedAt.AddTicks(1).ToString("O", CultureInfo.InvariantCulture);
                model.CapturedUntilText = summary.CapturedAt.ToString("O", CultureInfo.InvariantCulture);
                var beforeInverted = seen.Count;
                model.CanSearch.Should().BeFalse();
                await model.Refresh();
                seen.Should().HaveCount(beforeInverted);
                model.Status.Should().Contain("InvalidDataException");
                model.ClearQuery();
                field.Text = valid;
                model.Revoke();
                field.Text.Should().BeEmpty();
                field.Text = valid;
            }
            finally { window.Close(); }
            model.ObservationSourceText.Should().BeEmpty();
            model.DeviceProfileText.Should().BeEmpty();
            model.CapturedFromText.Should().BeEmpty();
            model.CapturedUntilText.Should().BeEmpty();
        });
    }

    [WindowsFact]
    public async Task TombstoneDetailsAndNativeRowsNeverExposePriorCorrelationMetadata()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            var row = Summary(fixture.Request) with { CopyStatus = AttachmentCopyStatus.Unavailable, StoredBytes = 0,
                CapturedAt = DateTimeOffset.UnixEpoch.AddYears(25) };
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
                model.SelectedDetails.Should().NotContain(row.ObservationSource.ToString("D"))
                    .And.NotContain(row.Profile.Value.ToString("D")).And.NotContain("1995-");
                Text(window).Should().NotContain("1995-");
                model.CanNavigate.Should().BeFalse();
            }
            finally { window.Close(); }
        });
    }
}
