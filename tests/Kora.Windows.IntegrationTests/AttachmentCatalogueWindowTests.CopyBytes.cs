using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;

using AwesomeAssertions;

using Kora.Core.Storage;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AttachmentCatalogueWindowTests
{
    private static readonly string[] AnyCopyLabel = ["Any"];
    private static readonly string[] InvalidByteBounds =
        [" ", "+1", "-0", "1,000", "1_000", "1.0", "1e2", "1\n", "1\0", "１", "١", "9223372036854775808", "262145"];

    [WindowsFact]
    public async Task NativeCopyAndByteFieldsAreAccessibleExplicitComposableAndRevokeAllObservationStateWithoutRead()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var summary = Summary(fixture.Request);
            var seen = new List<SessionAttachmentQuery>();
            var fail = false;
            var logger = new CatalogueLogger();
            using var model = new AttachmentCatalogueViewModel((query, cursor, _, _) =>
            {
                seen.Add(query);
                if (fail) { throw new IOException("PRIVATE_BYTES_QUERY_SENTINEL"); }
                return Task.FromResult(new SessionAttachmentPage(query, query.Matches(summary) ? [summary] : [],
                    cursor is null ? "YQ==." + new string('a', 64) : null,
                    DateTimeOffset.UnixEpoch, 1, new string('a', 64), 64, cursor is null));
            }, (_, _, _) => throw new InvalidOperationException("No implicit navigation."),
                access, () => true, logger);
            var revoked = 0;
            model.SelectionRevoked += _ => revoked++;
            var window = new AttachmentCatalogueWindow(model);
            window.Show();
            try
            {
                var picker = window.FindControl<ComboBox>("AttachmentCopyStatus")!;
                var minimum = window.FindControl<TextBox>("AttachmentMinimumBytes")!;
                var maximum = window.FindControl<TextBox>("AttachmentMaximumBytes")!;
                model.CopyStatuses.Select(choice => choice.ToString()).Should().Equal(
                    AnyCopyLabel.Concat(Enum.GetValues<AttachmentCopyStatus>().Select(value => value.ToString())));
                picker.SelectedItem.Should().Be(model.CopyStatuses[0], "Any must be a real visibly selected item, not a nullable binding");
                foreach (var control in new Control[] { picker, minimum, maximum })
                {
                    ControlAutomationPeer.CreatePeerForElement(control)!.GetName().Should().Contain("performs no read");
                    control.Focus();
                    control.IsFocused.Should().BeTrue();
                }
                seen.Should().BeEmpty();
                await model.Refresh();
                model.Select(summary);
                picker.SelectedItem = model.CopyStatuses.Single(choice => choice.Status == AttachmentCopyStatus.Confirmed);
                model.Records.Should().BeEmpty();
                model.SelectedRecord.Should().BeNull();
                model.CanNavigate.Should().BeFalse();
                model.CanNext.Should().BeFalse();
                revoked.Should().Be(1);
                seen.Should().HaveCount(1);
                model.Mode = AttachmentCatalogueMode.ExactId;
                model.QueryText = summary.Session.Value.ToString("D");
                model.Filter = SessionAttachmentFilter.Active;
                model.IncludeRemoved = true;
                model.MediaType = "text/plain";
                model.MetadataText = "text/";
                minimum.Text = "0001";
                maximum.Text = "1";
                seen.Should().HaveCount(1);
                minimum.Focus();
                minimum.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                await window.Operation;
                seen.Last().Should().Be(new SessionAttachmentQuery(true, model.QueryText, model.Filter, "text/", "text/plain",
                    AttachmentCopyStatus.Confirmed, 1, 1));
                model.Records.Should().ContainSingle();
                model.Select(summary);
                maximum.Text = "2";
                model.Records.Should().BeEmpty();
                model.SelectedDetails.Should().BeEmpty();
                model.CanNext.Should().BeFalse();
                model.CanNavigate.Should().BeFalse();
                revoked.Should().Be(2);
                seen.Should().HaveCount(2);
                await model.Refresh();
                await model.Refresh(next: true);
                model.ClearQuery();
                model.CopyStatus.Status.Should().Be(AttachmentCopyStatus.Confirmed);
                model.MinimumBytesText.Should().Be("0001");
                model.MaximumBytesText.Should().Be("2");
                foreach (var choice in model.CopyStatuses)
                {
                    picker.SelectedItem = choice;
                    await model.Refresh();
                    seen.Last().CopyStatus.Should().Be(choice.Status);
                }
                model.Status.Should().Contain("More slots remain");
                await model.Refresh(next: true);
                model.Status.Should().Contain("No matching retained metadata").And.Contain("not a global total");
                picker.SelectedItem = model.CopyStatuses[0];
                foreach (var invalid in InvalidByteBounds)
                {
                    foreach (var field in new[] { minimum, maximum })
                    {
                        minimum.Text = "";
                        maximum.Text = "";
                        field.Text = invalid;
                        var calls = seen.Count;
                        model.CanSearch.Should().BeFalse();
                        model.CopyAndBytesHint.Should().Contain("Invalid");
                        field.Focus();
                        field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                        await model.Refresh();
                        seen.Count.Should().Be(calls);
                        model.Status.Should().Contain("InvalidDataException");
                    }
                }
                minimum.Text = "2";
                maximum.Text = "1";
                model.CanSearch.Should().BeFalse();
                minimum.Text = "";
                maximum.Text = "";
                model.CopyStatus = new((AttachmentCopyStatus)99);
                model.CanSearch.Should().BeFalse();
                await model.Refresh();
                model.CopyStatus = null!;
                model.CanSearch.Should().BeFalse();
                await model.Refresh();
                picker.SelectedItem = model.CopyStatuses[0];
                model.MinimumBytesText = "0";
                model.MaximumBytesText = "262144";
                fail = true;
                await model.Refresh();
                model.Status.Should().Contain("IOException").And.NotContain("PRIVATE_BYTES_QUERY_SENTINEL");
                model.Records.Should().BeEmpty();
                logger.Messages.Should().OnlyContain(message => !message.Contains("PRIVATE_BYTES_QUERY_SENTINEL", StringComparison.Ordinal));
                model.Revoke();
                model.MinimumBytesText.Should().BeEmpty();
                model.MaximumBytesText.Should().BeEmpty();
                model.CopyStatus.Should().Be(model.CopyStatuses[0]);
            }
            finally { window.Close(); }
            model.CopyStatus.Should().Be(model.CopyStatuses[0]);
            model.MinimumBytesText.Should().BeEmpty();
            model.MaximumBytesText.Should().BeEmpty();
        });
    }

    [WindowsFact]
    public async Task NativeUnavailableProvenanceNeverDisplaysSentinelAsOriginalSize()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            var summary = Summary(fixture.Request) with
            { CopyStatus = AttachmentCopyStatus.Unavailable, StoredBytes = 0, SessionState = AttachmentSessionState.Removed };
            using var model = new AttachmentCatalogueViewModel((query, _, _, _) => Task.FromResult(
                new SessionAttachmentPage(query, query.Matches(summary) ? [summary] : [], null,
                    DateTimeOffset.UnixEpoch, 1, new string('a', 64), 1, false)),
                (_, _, _) => throw new InvalidOperationException("No tombstone navigation."),
                new WindowsSqliteSessionWorkspaceTests.Access(), () => true, NullLogger<AttachmentCatalogueViewModel>.Instance);
            var window = new AttachmentCatalogueWindow(model);
            window.Show();
            try
            {
                model.IncludeRemoved = true;
                model.Filter = SessionAttachmentFilter.Removed;
                model.CopyStatus = new(AttachmentCopyStatus.Unavailable);
                await model.Refresh();
                model.Select(summary);
                model.CanNavigate.Should().BeFalse();
                model.SelectedDetails.Should().NotContain("\"StoredBytes\"");
                window.UpdateLayout();
                Text(window).Should().Contain("no size claim").And.NotContain("Stored original bytes: 0");
                model.MaximumBytesText = "0";
                await model.Refresh();
                model.Records.Should().BeEmpty();
                model.Status.Should().Contain("No matching retained metadata");
            }
            finally { window.Close(); }
        });
    }
}
