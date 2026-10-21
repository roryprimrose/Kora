using Avalonia.Automation.Peers;
using Avalonia.Controls;
using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AttachmentCatalogueWindowTests
{
    [WindowsFact]
    public async Task ExactPreparationRetainsEveryOtherPredicateAndRetiresSelectionFindAndContinuationWithoutReading()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var summary = Summary(fixture.Request);
            var seen = new List<SessionAttachmentQuery>();
            var opens = 0;
            using var model = new AttachmentCatalogueViewModel((query, _, _, _) =>
            {
                seen.Add(query);
                return Task.FromResult(new SessionAttachmentPage(query, query.Matches(summary) ? [summary] : [],
                    "YQ==." + new string('a', 64), DateTimeOffset.UnixEpoch, 1, new string('a', 64), 1, false));
            }, (_, _, _) => { opens++; return Task.CompletedTask; },
                access, () => true, NullLogger<AttachmentCatalogueViewModel>.Instance);
            var window = new AttachmentCatalogueWindow(model);
            window.Show();
            try
            {
                seen.Should().BeEmpty();
                model.Filter = SessionAttachmentFilter.Active;
                model.IncludeRemoved = true;
                model.MediaType = "text/plain";
                model.CopyStatus = new(AttachmentCopyStatus.Confirmed);
                model.MinimumBytesText = "1";
                model.MaximumBytesText = "1";
                model.MetadataText = "text/";
                model.DigestText = summary.File.Digest;
                model.ObservationSourceText = summary.ObservationSource.ToString("D");
                model.DeviceProfileText = summary.Profile.Value.ToString("D");
                model.CapturedFromText = "1970-01-01T00:00:00.0000000+00:00";
                model.CapturedUntilText = "1970-01-01T00:00:00.0000000+00:00";
                model.MinimumSessionGenerationText = "1";
                model.MaximumSessionGenerationText = "1";
                model.MinimumCapturedGenerationText = "1";
                model.MaximumCapturedGenerationText = "1";
                model.MinimumStorageRevisionText = "1";
                model.MaximumStorageRevisionText = "1";
                model.GenerationRelationship = new(AttachmentGenerationRelationship.SameObservedGeneration);
                model.FileSourceIdText = summary.File.SourceId.ToString("D");
                model.FileRevisionIdText = summary.File.RevisionId.ToString("D");
                model.FileItemIdText = summary.File.ItemId.ToString("D");
                await model.Refresh();
                var before = seen.Single();
                model.Records.Should().ContainSingle();
                model.Select(summary);
                model.MetadataFindQuery = "text";
                model.FindMetadata(SnapshotFindDirection.First);
                model.CanNavigate.Should().BeTrue();
                model.CanNext.Should().BeTrue();
                model.HasMetadataFindMatch.Should().BeTrue();
                var revoked = 0;
                model.SelectionRevoked += exact => { exact.Should().BeSameAs(summary); revoked++; };
                window.PrepareExactSession(summary.Session.Value, () => true);
                seen.Should().HaveCount(1);
                opens.Should().Be(0);
                revoked.Should().Be(1);
                model.Mode.Should().Be(AttachmentCatalogueMode.ExactId);
                model.QueryText.Should().Be(summary.Session.Value.ToString("D"));
                model.Records.Should().BeEmpty();
                model.SelectedRecord.Should().BeNull();
                model.SelectedDetails.Should().BeEmpty();
                model.MetadataFindQuery.Should().BeEmpty();
                model.CanFindMetadata.Should().BeFalse();
                model.CanNext.Should().BeFalse();
                model.CanNavigate.Should().BeFalse();
                model.Status.Should().Contain("No I/O").And.Contain("AND-composed");
                var field = window.FindControl<TextBox>("AttachmentQueryText")!;
                field.IsFocused.Should().BeTrue();
                ControlAutomationPeer.CreatePeerForElement(field)!.GetName().Should().Contain("exact session ID");
                await model.Refresh();
                var after = seen.Last();
                after.Should().BeEquivalentTo(before, options => options
                    .Excluding(query => query.ExactId).Excluding(query => query.ExactSessionId));
                after.ExactId.Should().Be(summary.Session.Value);
                model.Records.Should().ContainSingle();
                // A future ID is just another AND predicate; it cannot fall back to Browse.
                window.PrepareExactSession(Guid.NewGuid(), () => true);
                await model.Refresh();
                model.Records.Should().BeEmpty();
                opens.Should().Be(0);
                model.MaximumBytesText = "invalid unsaved draft";
                window.PrepareExactSession(summary.Session.Value, () => true);
                model.MaximumBytesText.Should().Be("invalid unsaved draft");
                model.CanSearch.Should().BeFalse();
                seen.Should().HaveCount(3);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreparationCancelsOldPageAndOpenCallbacksAndRejectsTheirLateResults(bool navigation)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var summary = Summary(fixture.Request);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var lateOpen = false;
        CancellationToken oldToken = default;
        using var model = new AttachmentCatalogueViewModel(async (query, _, _, token) =>
        {
            if (++reads > 1)
            {
                oldToken = token;
                entered.SetResult();
                await release.Task.WaitAsync(fixture.Token);
            }
            return new SessionAttachmentPage(query, [summary], null, DateTimeOffset.UnixEpoch, 1, new string('a', 64), 1, false);
        }, async (_, current, token) =>
        {
            oldToken = token;
            entered.SetResult();
            await release.Task.WaitAsync(fixture.Token);
            lateOpen = current();
        }, access, () => true, NullLogger<AttachmentCatalogueViewModel>.Instance);
        await model.Refresh();
        model.Select(summary);
        var pending = navigation ? model.Navigate() : model.Refresh();
        await entered.Task.WaitAsync(fixture.Token);
        var id = Guid.NewGuid();
        model.PrepareExactSession(id);
        oldToken.IsCancellationRequested.Should().BeTrue();
        model.Records.Should().BeEmpty();
        model.SelectedDetails.Should().BeEmpty();
        release.SetResult();
        await pending;
        model.QueryText.Should().Be(id.ToString("D"));
        model.Mode.Should().Be(AttachmentCatalogueMode.ExactId);
        model.Status.Should().Contain("PREPARED");
        model.Records.Should().BeEmpty();
        lateOpen.Should().BeFalse();
        reads.Should().Be(navigation ? 1 : 2);
    }

    [WindowsFact]
    public async Task NativePreparationRefusesMissingAdmissionInvalidIdDisposedModelAndUnavailableFocus()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            using var model = new AttachmentCatalogueViewModel((_, _, _, _) => throw new InvalidOperationException("No read"),
                (_, _, _) => throw new InvalidOperationException("No open"), access, () => true,
                NullLogger<AttachmentCatalogueViewModel>.Instance);
            var window = new AttachmentCatalogueWindow(model);
            var id = Guid.NewGuid();
            Action prepare = () => window.PrepareExactSession(id, () => true);
            prepare.Should().Throw<InvalidOperationException>();
            window.Show();
            try
            {
                prepare = () => window.PrepareExactSession(id, () => false);
                prepare.Should().Throw<InvalidOperationException>();
                prepare = () => model.PrepareExactSession(Guid.Empty);
                prepare.Should().Throw<InvalidOperationException>();
                model.MetadataText = "unsaved companion draft";
                model.MaximumBytesText = "invalid draft";
                window.FindControl<TextBox>("AttachmentQueryText")!.Focusable = false;
                prepare = () => window.PrepareExactSession(id, () => true);
                prepare.Should().Throw<InvalidOperationException>();
                model.Status.Should().NotContain("PREPARED");
                model.MetadataText.Should().Be("unsaved companion draft");
                model.MaximumBytesText.Should().Be("invalid draft");
                var field = window.FindControl<TextBox>("AttachmentQueryText")!;
                field.Focusable = true;
                field.GotFocus += (_, _) => model.QueryText = Guid.NewGuid().ToString("D");
                prepare.Should().Throw<InvalidOperationException>();
                model.Status.Should().NotContain("PREPARED");
                model.Dispose();
                prepare = () => model.PrepareExactSession(id);
                prepare.Should().Throw<InvalidOperationException>();
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        });
    }
}
