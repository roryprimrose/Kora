using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests;

public sealed partial class WorkspaceSurfaceCommandControllerTests
{
    private const string PrepareDraft = "prepare saved draft catalogue abcdef12-3456-7890-abcd-ef1234567890";

    [WindowsFact]
    public async Task SavedCataloguePreparationAcknowledgesSameAccessibleNativeWindowWithZeroStorageCallsAndAllFilterDraftsRetained()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NavigationFixture();
            await f.Native.InitializeAsync();
            var store = new RefusingWorkspaceStore();
            f.Bind(new(store, new(f.Native.Tasks), f.Native.Access,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Kora.Application.Hosting.SessionWorkspaceService>.Instance));
            SavedDraftCatalogueWindow? window = null;
            var opens = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SavedDraftCatalogueWindow>((view, _) =>
            { window = view; opens++; });
            await f.Run("saved draft catalogue help");
            opens.Should().Be(0);
            await f.Run(PrepareDraft);
            window.Should().NotBeNull();
            window!.IsVisible.Should().BeTrue();
            var model = (SavedDraftCatalogueViewModel)window!.DataContext!;
            model.CanEditQuery.Should().BeTrue();
            var initialQuery = window.FindControl<TextBox>("DraftQueryText")!;
            initialQuery.IsEnabled.Should().BeTrue();
            initialQuery.IsFocused.Should().BeTrue();
            f.Native.Main.Transcript.Should().Contain("no I/O yet");
            model.Mode.Should().Be(SavedDraftCatalogueMode.ExactId);
            model.Filter.Should().Be(SessionListFilter.All);
            FilterDrafts(model).Should().OnlyContain(value => value.Length == 0);
            model.Filter = SessionListFilter.Done;
            model.RevisionText = "invalid retained draft";
            model.MinimumBytesText = "1";
            model.MaximumBytesText = "20";
            model.MinimumScalarsText = "1";
            model.MaximumScalarsText = "20";
            model.SourceText = Guid.NewGuid().ToString("D");
            model.ProfileText = Guid.NewGuid().ToString("D");
            model.MinimumSessionGenerationText = "1";
            model.MaximumSessionGenerationText = "2";
            model.MinimumNameMetadataRevisionText = "3";
            model.MaximumNameMetadataRevisionText = "4";
            model.MinimumControlRevisionText = "0";
            model.MaximumControlRevisionText = "6";
            model.MinimumAuthorityCeilingText = "7";
            model.MaximumAuthorityCeilingText = "8";
            var before = FilterDrafts(model);
            model.Mode = SavedDraftCatalogueMode.NameSubstring;
            model.QueryText = "different draft";
            await f.Run(PrepareDraft);
            opens.Should().Be(1);
            model.Mode.Should().Be(SavedDraftCatalogueMode.ExactId);
            model.QueryText.Should().Be("abcdef12-3456-7890-abcd-ef1234567890");
            model.Filter.Should().Be(SessionListFilter.Done);
            FilterDrafts(model).Should().Equal(before);
            model.Status.Should().Contain("no I/O yet").And.Contain("AND-composed");
            model.CanSearch.Should().BeFalse("invalid retained filter drafts must still refuse search");
            var input = window.FindControl<TextBox>("DraftQueryText")!;
            input.Text.Should().Be(model.QueryText);
            input.IsFocused.Should().BeTrue();
            ControlAutomationPeer.CreatePeerForElement(input)!.GetName().Should().NotBeNullOrWhiteSpace();
            model.Records.Should().BeEmpty();
            model.SelectedRecord.Should().BeNull();
            model.SelectedDetails.Should().BeEmpty();
            model.CanNavigate.Should().BeFalse();
            model.CanNext.Should().BeFalse();
            store.Calls.Should().Be(0);
            window.Close();
            model.QueryText.Should().BeEmpty();
            FilterDrafts(model).Should().OnlyContain(value => value.Length == 0);
        });
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("input")]
    [InlineData("close")]
    [InlineData("controller")]
    [InlineData("binding")]
    [InlineData("focus")]
    [InlineData("during-open")]
    [InlineData("query-source")]
    [InlineData("source")]
    [InlineData("hide-show")]
    public async Task SavedCatalogueQueuedPreparationCannotAcquireNewSourceLifetimeOrReportFalseNativeSuccess(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NavigationFixture();
            await f.Native.InitializeAsync();
            f.Bind();
            SavedDraftCatalogueWindow? window = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SavedDraftCatalogueWindow>((view, _) =>
            {
                window = view;
                if (string.Equals(boundary, "during-open", StringComparison.Ordinal)) { view.Close(); }
            });
            if (!string.Equals(boundary, "during-open", StringComparison.Ordinal)) { await f.Run("open saved conversation drafts"); }
            var model = window?.DataContext as SavedDraftCatalogueViewModel;
            f.Native.Main.CommandText = PrepareDraft;
            var preparing = f.Native.Main.RunTypedCommand.ExecuteAsync();
            preparing.IsCompleted.Should().BeFalse();
            switch (boundary)
            {
                case "owner": f.Native.Access.SetOpen(false); f.Native.Access.SetOpen(true); break;
                case "input": f.Native.Main.CommandText = "different input"; break;
                case "close": window!.Close(); break;
                case "hide-show": window!.Hide(); window.Show(); break;
                case "controller": f.Drafts!.Dispose(); break;
                case "binding": f.Commands!.Dispose(); break;
                case "focus": window!.FindControl<TextBox>("DraftQueryText")!.Focusable = false; break;
                case "query-source": model!.Mode = SavedDraftCatalogueMode.ExactId; break;
                case "source":
                    typeof(SavedDraftCatalogueWindowController).GetMethod("OnSourceChanged",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(f.Drafts, [f.Native.LifecycleTarget!.SessionId]);
                    break;
            }
            await preparing;
            f.Native.Main.Transcript.Should().Contain("unavailable").And.NotContain("prepared;");
            model?.QueryText.Should().BeEmpty();
        });
    }

    private static string[] FilterDrafts(SavedDraftCatalogueViewModel model) =>
    [
        model.RevisionText, model.MinimumBytesText, model.MaximumBytesText, model.MinimumScalarsText,
        model.MaximumScalarsText, model.SourceText, model.ProfileText, model.MinimumSessionGenerationText,
        model.MaximumSessionGenerationText, model.MinimumNameMetadataRevisionText, model.MaximumNameMetadataRevisionText,
        model.MinimumControlRevisionText, model.MaximumControlRevisionText,
        model.MinimumAuthorityCeilingText, model.MaximumAuthorityCeilingText,
    ];
}
