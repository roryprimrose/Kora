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
    [WindowsFact]
    public async Task NativeDigestIsAccessibleExplicitStrictComposableAndRetiresSelectionWithoutReads()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var summary = Summary(fixture.Request);
            var seen = new List<SessionAttachmentQuery>();
            var logger = new CatalogueLogger();
            using var model = new AttachmentCatalogueViewModel((query, cursor, _, _) =>
            {
                seen.Add(query);
                return Task.FromResult(new SessionAttachmentPage(query, query.Matches(summary) ? [summary] : [],
                    cursor is null ? "YQ==." + new string('b', 64) : null,
                    DateTimeOffset.UnixEpoch, 1, new string('b', 64), 1, false));
            }, (_, _, _) => throw new InvalidOperationException("No implicit navigation."),
                access, () => true, logger);
            var revoked = 0;
            model.SelectionRevoked += _ => revoked++;
            var window = new AttachmentCatalogueWindow(model);
            window.Show();
            try
            {
                var field = window.FindControl<TextBox>("AttachmentDigest")!;
                ControlAutomationPeer.CreatePeerForElement(field)!.GetName().Should().Contain("exact SHA-256").And.Contain("performs no read");
                field.Focus();
                field.IsFocused.Should().BeTrue();
                seen.Should().BeEmpty();
                await model.Refresh();
                model.Select(summary);
                field.Text = summary.File.Digest;
                model.Records.Should().BeEmpty();
                model.SelectedDetails.Should().BeEmpty();
                model.CanNavigate.Should().BeFalse();
                model.CanNext.Should().BeFalse();
                revoked.Should().Be(1);
                seen.Should().HaveCount(1);
                model.Mode = AttachmentCatalogueMode.ExactId;
                model.QueryText = summary.Session.Value.ToString("D");
                model.Filter = SessionAttachmentFilter.Active;
                model.IncludeRemoved = true;
                model.MetadataText = "text/";
                model.MediaType = "text/plain";
                model.CopyStatus = model.CopyStatuses.Single(choice => choice.Status == AttachmentCopyStatus.Confirmed);
                model.MinimumBytesText = "1";
                model.MaximumBytesText = "1";
                field.Focus();
                field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                await window.Operation;
                seen.Last().Should().Be(new SessionAttachmentQuery(true, model.QueryText, model.Filter, "text/", "text/plain",
                    AttachmentCopyStatus.Confirmed, 1, 1, summary.File.Digest));
                model.Select(summary);
                field.Text = summary.File.Digest;
                revoked.Should().Be(1);
                await model.Refresh(next: true);
                seen.Last().ExactDigest.Should().Be(summary.File.Digest);
                model.Select(summary);
                field.Text = new string('b', 64);
                revoked.Should().Be(3);
                await model.Refresh();
                await model.Refresh(next: true);
                model.Status.Should().Contain("No matching retained metadata").And.Contain("not a global total");
                foreach (var invalid in new[] { " ", new string('0', 64), new string('A', 64), new string('a', 63),
                    new string('a', 65), new string('a', 64) + "\n", new string('a', 63) + "\u200b" })
                {
                    field.Text = invalid;
                    var reads = seen.Count;
                    model.CanSearch.Should().BeFalse();
                    model.DigestHint.Should().Contain("Invalid");
                    field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                    await model.Refresh();
                    seen.Should().HaveCount(reads);
                    model.Status.Should().Contain("InvalidDataException");
                }
                model.ClearQuery();
                field.Text.Should().BeEmpty();
                model.DigestText = summary.File.Digest;
                model.Revoke();
                model.DigestText.Should().BeEmpty();
                model.DigestText = summary.File.Digest;
                logger.Messages.Should().OnlyContain(message => !message.Contains(summary.File.Digest, StringComparison.Ordinal));
            }
            finally { window.Close(); }
            model.DigestText.Should().BeEmpty();
        });
    }

    [WindowsFact]
    public async Task RemovedNativeDetailsNeverExposeHistoricalDigest()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            var row = Summary(fixture.Request) with { CopyStatus = AttachmentCopyStatus.Unavailable, StoredBytes = 0 };
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
                model.SelectedDetails.Should().NotContain(row.File.Digest);
                Text(window).Should().NotContain(row.File.Digest);
                model.CanNavigate.Should().BeFalse();
            }
            finally { window.Close(); }
        });
    }
}
