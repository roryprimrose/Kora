using System.Globalization;

using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task PreparedExactIdRetainsAllPredicatesAndSearchStillUsesTheirANDComposition()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var session = f.Request.SessionId;
        await WindowsSqliteSessionWorkspaceTests.Service(f, new()).RenameAsync(session, new(1), 0,
            new("Prepared metadata"), RequestOrigin.LocalUi, f.Token);
        await WindowsSqliteConversationTests.Save(f,
            await f.Store.ReadConversationDraftAsync(session, f.Token), "saved private body");
        var checkpoint = new CatalogueCheckpoint();
        var reads = 0;
        checkpoint.Read = () => reads++;
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        using var model = new SavedDraftCatalogueViewModel(WindowsSqliteSessionWorkspaceTests.Service(f, access),
            (_, _, _) => throw new InvalidOperationException("Preparation/search must not navigate."),
            access, () => true, NullLogger<SavedDraftCatalogueViewModel>.Instance);
        await model.Refresh();
        var row = model.Records.Single();
        model.Filter = SessionListFilter.Active;
        model.RevisionText = row.DraftRevision.ToString(CultureInfo.InvariantCulture);
        model.MinimumBytesText = model.MaximumBytesText = row.Utf8Bytes.ToString(CultureInfo.InvariantCulture);
        model.MinimumScalarsText = model.MaximumScalarsText = row.UnicodeScalars.ToString(CultureInfo.InvariantCulture);
        model.SourceText = row.Source.ToString("D");
        model.ProfileText = row.Profile.Value.ToString("D");
        model.MinimumSessionGenerationText = model.MaximumSessionGenerationText = row.Session.Generation.Value.ToString(CultureInfo.InvariantCulture);
        model.MinimumNameMetadataRevisionText = model.MaximumNameMetadataRevisionText = row.Metadata!.Revision.Value.ToString(CultureInfo.InvariantCulture);
        model.MinimumControlRevisionText = model.MaximumControlRevisionText = row.ControlRevision.ToString(CultureInfo.InvariantCulture);
        model.MinimumAuthorityCeilingText = model.MaximumAuthorityCeilingText = row.AuthorityCeiling.ToString(CultureInfo.InvariantCulture);
        var before = reads;
        model.PrepareExactId(session.Value).Should().BeTrue();
        reads.Should().Be(before);
        model.Records.Should().BeEmpty();
        model.SelectedDetails.Should().BeEmpty();
        await model.Refresh();
        model.Records.Should().ContainSingle().Which.Should().Be(row);
        model.Select(model.Records.Single());
        model.CanNavigate.Should().BeTrue();
        model.PrepareExactId(session.Value).Should().BeTrue("same ID must retire the existing observation");
        model.SelectedRecord.Should().BeNull();
        model.SelectedDetails.Should().BeEmpty();
        model.CanNavigate.Should().BeFalse();
        model.CanNext.Should().BeFalse();
        model.Filter = SessionListFilter.Done;
        model.PrepareExactId(session.Value).Should().BeTrue();
        await model.Refresh();
        model.Records.Should().BeEmpty("lifecycle remains AND-composed");
        model.Filter = SessionListFilter.Active;
        model.RevisionText = (row.DraftRevision + 1).ToString(CultureInfo.InvariantCulture);
        model.PrepareExactId(session.Value).Should().BeTrue();
        await model.Refresh();
        model.Records.Should().BeEmpty("the retained exact saved revision remains AND-composed");
        model.Status.Should().Contain("not found").And.Contain("no fallback");
        model.PrepareExactId(Guid.Empty).Should().BeFalse();
        access.CanInspect = false;
        model.PrepareExactId(session.Value).Should().BeFalse();
        model.Dispose();
        model.PrepareExactId(session.Value).Should().BeFalse();
        f.Count("conversation_messages").Should().Be(0);
    }
}
