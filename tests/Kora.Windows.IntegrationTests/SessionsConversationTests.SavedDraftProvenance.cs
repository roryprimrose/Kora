using System.Globalization;

using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Controls;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    private static readonly string[] ZeroDraftProvenanceBound = ["0"];
    [Fact]
    public async Task SavedDraftProvenanceControlsAreAccessibleStrictInclusiveAndRetireExactSelectionWithoutReads()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await WindowsSqliteConversationTests.Save(f,
                await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "🙂");
            await WindowsSqliteSessionWorkspaceTests.Service(f, new()).RenameAsync(f.Request.SessionId,
                new(1), 0, new("private needle"), RequestOrigin.LocalUi, f.Token);
            var checkpoint = new CatalogueCheckpoint();
            var reads = 0;
            checkpoint.Read = () => reads++;
            f.Reopen(checkpoint);
            await f.Store.InitializeAsync(f.Token);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var navigations = 0;
            using var model = new SavedDraftCatalogueViewModel(WindowsSqliteSessionWorkspaceTests.Service(f, access),
                (_, _, _) => { navigations++; return Task.FromResult(false); }, access, () => true,
                NullLogger<SavedDraftCatalogueViewModel>.Instance);
            var window = new SavedDraftCatalogueWindow(model);
            window.Show();
            try
            {
                await model.Refresh();
                var row = model.Records.Single();
                window.Width = window.MinWidth;
                window.Height = window.MinHeight;
                window.UpdateLayout();
                var fields = new (string Name, long Value)[]
                {
                    ("MinimumSessionGeneration", row.Session.Generation.Value),
                    ("MaximumSessionGeneration", row.Session.Generation.Value),
                    ("MinimumNameMetadataRevision", row.Metadata!.Revision.Value),
                    ("MaximumNameMetadataRevision", row.Metadata.Revision.Value),
                    ("MinimumControlRevision", row.ControlRevision), ("MaximumControlRevision", row.ControlRevision),
                    ("MinimumAuthorityCeiling", row.AuthorityCeiling), ("MaximumAuthorityCeiling", row.AuthorityCeiling),
                };
                foreach (var (name, value) in fields)
                {
                    var control = window.FindControl<TextBox>("Draft" + name + "Text")!;
                    control.Focusable.Should().BeTrue();
                    control.IsEnabled.Should().BeTrue();
                    control.BringIntoView();
                    window.UpdateLayout();
                    var location = control.TranslatePoint(default, window)!.Value;
                    location.Y.Should().BeGreaterThanOrEqualTo(0);
                    (location.Y + control.Bounds.Height).Should().BeLessThanOrEqualTo(window.ClientSize.Height);
                    window.FindControl<ScrollableListBox>("DraftRecords")!.Bounds.Height.Should().BeGreaterThan(0);
                    ControlAutomationPeer.CreatePeerForElement(control)!.GetName().Should().Contain("Inclusive")
                        .And.Contain("empty means Any").And.Contain("provenance only").And.Contain("typing performs no read");
                    foreach (var invalid in InvalidDraftMetadataIntegers.Concat(
                        name.Contains("Control", StringComparison.Ordinal) ? [] : ZeroDraftProvenanceBound))
                    {
                        var before = reads;
                        control.Text = invalid;
                        window.FindControl<Button>("RefreshPage")!.IsEnabled.Should().BeFalse();
                        await model.Refresh();
                        reads.Should().Be(before);
                        model.Status.Should().Contain("InvalidDataException");
                        control.Text = "";
                    }
                    await model.Refresh();
                    model.Select(model.Records.Single());
                    var observedReads = reads;
                    control.Text = value.ToString(CultureInfo.InvariantCulture);
                    reads.Should().Be(observedReads);
                    model.Records.Should().BeEmpty();
                    model.SelectedRecord.Should().BeNull();
                    model.SelectedDetails.Should().BeEmpty();
                    model.CanNavigate.Should().BeFalse();
                    await model.Navigate();
                    navigations.Should().Be(0, "retired selection cannot navigate even with matching provenance");
                    await model.Refresh();
                    model.Records.Should().ContainSingle();
                    model.Select(model.Records.Single());
                    model.Revoke();
                    control.Text.Should().BeEmpty();
                }
                model.MinimumSessionGenerationText = "2";
                model.MaximumSessionGenerationText = "1";
                model.CanSearch.Should().BeFalse();
                model.ClearQuery();
                model.MinimumSessionGenerationText.Should().BeEmpty();
                model.MaximumSessionGenerationText.Should().BeEmpty();
                model.MinimumNameMetadataRevisionText = "2";
                model.MaximumNameMetadataRevisionText = "1";
                model.CanSearch.Should().BeFalse();
                model.ClearQuery();
                model.MinimumControlRevisionText = "2";
                model.MaximumControlRevisionText = "1";
                model.CanSearch.Should().BeFalse();
                model.ClearQuery();
                model.MinimumAuthorityCeilingText = "2";
                model.MaximumAuthorityCeilingText = "1";
                model.CanSearch.Should().BeFalse();
                model.ClearQuery();
                model.MinimumControlRevisionText = "0";
                model.CanSearch.Should().BeTrue();
                model.MetadataHint.Should().Contain("not current authority").And.Contain("body integrity");
                f.Count("conversation_messages").Should().Be(0);
            }
            finally { window.Close(); }
        });
    }
}
