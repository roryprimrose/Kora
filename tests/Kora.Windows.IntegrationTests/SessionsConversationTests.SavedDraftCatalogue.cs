using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Storage;
using Kora.Controls;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    private static readonly string[] InvalidDraftMetadataIntegers =
        [" ", " 1", "+1", "-1", "1,000", "1_000", "1.5", "1e2", "1 ", "1\n", "1\0", "１", "١", "9223372036854775808"];
    private static readonly string[] InvalidDraftMetadataGuids =
    [
        "00000000-0000-0000-0000-000000000000", "ABCDEF12-3456-7890-ABCD-EF1234567890",
        "{abcdef12-3456-7890-abcd-ef1234567890}", "(abcdef12-3456-7890-abcd-ef1234567890)",
        "abcdef1234567890abcdef1234567890", "abcdef12", " abcdef12-3456-7890-abcd-ef1234567890",
        "abcdef12-3456-7890-abcd-ef1234567890 ", "abcdef12-3456-7890-abcd-ef1234567890\n",
        "\tabcdef12-3456-7890-abcd-ef1234567890", " ", "\u200b",
    ];

    [Fact]
    public async Task NativeSavedDraftGuidControlsAreAccessibleStrictAndMetadataOnly()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await WindowsSqliteConversationTests.Save(f,
                await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "🙂");
            var checkpoint = new CatalogueCheckpoint();
            var reads = 0;
            checkpoint.Read = () => reads++;
            f.Reopen(checkpoint);
            await f.Store.InitializeAsync(f.Token);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            using var model = new SavedDraftCatalogueViewModel(WindowsSqliteSessionWorkspaceTests.Service(f, access),
                (_, _, _) => Task.FromResult(false), access, () => true, NullLogger<SavedDraftCatalogueViewModel>.Instance);
            var window = new SavedDraftCatalogueWindow(model);
            window.Show();
            try
            {
                var source = window.FindControl<TextBox>("DraftSourceText")!;
                var profile = window.FindControl<TextBox>("DraftProfileText")!;
                var search = window.FindControl<Button>("RefreshPage")!;
                window.UpdateLayout();
                window.FindControl<ScrollableListBox>("DraftRecords")!.Bounds.Height.Should().BeGreaterThan(0);
                foreach (var control in new[] { source, profile })
                {
                    control.Focusable.Should().BeTrue();
                    control.IsEffectivelyVisible.Should().BeTrue();
                    var location = control.TranslatePoint(default, window)!.Value;
                    location.Y.Should().BeGreaterThanOrEqualTo(0);
                    (location.Y + control.Bounds.Height).Should().BeLessThanOrEqualTo(window.ClientSize.Height);
                    ControlAutomationPeer.CreatePeerForElement(control)!.GetName().Should()
                        .Contain("Exact").And.Contain("GUID").And.Contain("empty means Any").And.Contain("typing performs no read");
                    foreach (var invalid in InvalidDraftMetadataGuids)
                    {
                        control.Text = invalid;
                        search.IsEnabled.Should().BeFalse();
                        model.MetadataHint.Should().Contain("refused");
                        await model.Refresh();
                        model.Status.Should().Contain("InvalidDataException");
                        reads.Should().Be(0);
                        control.Text = "";
                    }
                }
                await model.Refresh();
                var row = model.Records.Single();
                foreach (var control in new[] { source, profile })
                {
                    model.Select(model.Records.Single());
                    control.Text = control == source ? row.Source.ToString("D") : row.Profile.Value.ToString("D");
                    model.Records.Should().BeEmpty();
                    model.SelectedRecord.Should().BeNull();
                    model.CanNavigate.Should().BeFalse();
                    await model.Refresh();
                    model.Records.Should().ContainSingle();
                }
                var before = reads;
                source.Text = Guid.NewGuid().ToString("D");
                reads.Should().Be(before);
                await model.Refresh();
                model.Records.Should().BeEmpty();
                model.Status.Should().Contain("0 saved drafts").And.Contain("End");
                window.FindControl<Button>("ClearQuery")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                source.Text.Should().BeEmpty();
                profile.Text.Should().BeEmpty();
                source.Text = row.Source.ToString("D");
                profile.Text = row.Profile.Value.ToString("D");
                window.Close();
                model.SourceText.Should().BeEmpty();
                model.ProfileText.Should().BeEmpty();
                f.Count("conversation_messages").Should().Be(0);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task NativeSavedDraftMetadataControlsAreAccessibleStrictAndRevokeWithoutReading()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await WindowsSqliteConversationTests.Save(f,
                await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "🙂");
            var checkpoint = new CatalogueCheckpoint();
            var reads = 0;
            checkpoint.Read = () => reads++;
            f.Reopen(checkpoint);
            await f.Store.InitializeAsync(f.Token);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            using var model = new SavedDraftCatalogueViewModel(WindowsSqliteSessionWorkspaceTests.Service(f, access),
                (_, _, _) => Task.FromResult(false), access, () => true, NullLogger<SavedDraftCatalogueViewModel>.Instance);
            var window = new SavedDraftCatalogueWindow(model);
            window.Show();
            try
            {
                var revision = window.FindControl<TextBox>("DraftRevisionText")!;
                var minimum = window.FindControl<TextBox>("DraftMinimumBytesText")!;
                var maximum = window.FindControl<TextBox>("DraftMaximumBytesText")!;
                var minimumScalars = window.FindControl<TextBox>("DraftMinimumScalarsText")!;
                var maximumScalars = window.FindControl<TextBox>("DraftMaximumScalarsText")!;
                var search = window.FindControl<Button>("RefreshPage")!;
                foreach (var control in new[] { revision, minimum, maximum, minimumScalars, maximumScalars })
                {
                    ControlAutomationPeer.CreatePeerForElement(control)!.GetName().Should().NotBeNullOrWhiteSpace();
                    control.Focusable.Should().BeTrue();
                    control.IsEnabled.Should().BeTrue();
                    foreach (var invalid in InvalidDraftMetadataIntegers)
                    {
                        control.Text = invalid;
                        search.IsEnabled.Should().BeFalse();
                        model.MetadataHint.Should().Contain("refused");
                        await model.Refresh();
                        model.Status.Should().Contain("InvalidDataException");
                        reads.Should().Be(0);
                        control.Text = "";
                    }
                }
                revision.Text = "0";
                model.CanSearch.Should().BeFalse();
                revision.Text = "1";
                minimum.Text = "5";
                maximum.Text = "4";
                model.CanSearch.Should().BeFalse();
                minimum.Text = "4";
                maximum.Text = "16385";
                model.CanSearch.Should().BeFalse();
                maximum.Text = "4";
                minimumScalars.Text = "4097";
                model.CanSearch.Should().BeFalse();
                minimumScalars.Text = "2";
                maximumScalars.Text = "1";
                model.CanSearch.Should().BeFalse();
                minimumScalars.Text = "1";
                maximumScalars.Text = "4097";
                model.CanSearch.Should().BeFalse();
                maximumScalars.Text = "1";
                reads.Should().Be(0);
                search.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.Operation;
                model.Records.Should().ContainSingle().Which.Utf8Bytes.Should().Be(4);
                model.Records.Single().UnicodeScalars.Should().Be(1, "the emoji is one scalar, not two UTF-16 code units or four bytes");
                model.Select(model.Records.Single());
                maximumScalars.Text = "";
                minimumScalars.Text = "2";
                model.Records.Should().BeEmpty();
                model.SelectedRecord.Should().BeNull();
                model.CanNavigate.Should().BeFalse();
                model.CanNext.Should().BeFalse();
                reads.Should().Be(1);
                search.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.Operation;
                model.Records.Should().BeEmpty();
                model.Status.Should().Contain("0 saved drafts").And.Contain("End");
                window.FindControl<Button>("ClearQuery")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.RevisionText.Should().BeEmpty();
                model.MinimumBytesText.Should().BeEmpty();
                model.MaximumBytesText.Should().BeEmpty();
                model.MinimumScalarsText.Should().BeEmpty();
                model.MaximumScalarsText.Should().BeEmpty();
                reads.Should().Be(2);
                revision.Text = "1";
                minimum.Text = "0";
                maximum.Text = "16384";
                minimumScalars.Text = "0";
                maximumScalars.Text = "4096";
                window.Close();
                model.RevisionText.Should().BeEmpty();
                model.MinimumBytesText.Should().BeEmpty();
                model.MaximumBytesText.Should().BeEmpty();
                model.MinimumScalarsText.Should().BeEmpty();
                model.MaximumScalarsText.Should().BeEmpty();
                f.Count("conversation_messages").Should().Be(0);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task SavedDraftMetadataEditsImmediatelyRevokeRealContinuationAndSelectionWithoutARead()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        for (var index = 0; index < SavedDraftPage.MaximumRecords + 1; index++)
        {
            if (index != 0)
            {
                f.Request = InteractionStorageFixture.NewRequest();
                await f.AdmitAsync(newSession: true);
                await WindowsSqliteConversationTests.FinishInitial(f);
            }
            await WindowsSqliteConversationTests.Save(f,
                await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "🙂");
        }
        var checkpoint = new CatalogueCheckpoint();
        var reads = 0;
        checkpoint.Read = () => reads++;
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        using var model = new SavedDraftCatalogueViewModel(WindowsSqliteSessionWorkspaceTests.Service(f, access),
            (_, _, _) => Task.FromResult(false), access, () => true, NullLogger<SavedDraftCatalogueViewModel>.Instance);
        foreach (var field in new[] { "minimum", "maximum", "source", "profile" })
        {
            await model.Refresh();
            var row = model.Records[0];
            model.Select(row);
            model.CanNext.Should().BeTrue();
            model.CanNavigate.Should().BeTrue();
            var observedReads = reads;
            switch (field)
            {
                case "minimum": model.MinimumScalarsText = "1"; break;
                case "maximum": model.MaximumScalarsText = "4096"; break;
                case "source": model.SourceText = row.Source.ToString("D"); break;
                default: model.ProfileText = row.Profile.Value.ToString("D"); break;
            }
            reads.Should().Be(observedReads);
            model.Records.Should().BeEmpty();
            model.SelectedRecord.Should().BeNull();
            model.CanNext.Should().BeFalse();
            model.CanNavigate.Should().BeFalse();
            model.CanSearch.Should().BeTrue();
        }
        model.ClearQuery();
        model.MinimumScalarsText.Should().BeEmpty();
        model.MaximumScalarsText.Should().BeEmpty();
        model.SourceText.Should().BeEmpty();
        model.ProfileText.Should().BeEmpty();
        f.Count("conversation_messages").Should().Be(0);
    }

    [Fact]
    public async Task ActualNativeDraftQueryControlsReuseMetadataPolicyWithoutTypingReadsOrComposerChanges()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var original = f.Request.SessionId;
            await WindowsSqliteSessionWorkspaceTests.Service(f, new()).RenameAsync(original, new(1), 0,
                new("Private café 🙂 100%_"), Kora.Core.Hosting.RequestOrigin.LocalUi, f.Token);
            await WindowsSqliteConversationTests.Save(f,
                await f.Store.ReadConversationDraftAsync(original, f.Token), "PRIVATE_NOT_A_QUERY_RESULT");
            var checkpoint = new CatalogueCheckpoint();
            var reads = 0;
            checkpoint.Read = () => reads++;
            f.Reopen(checkpoint);
            await f.Store.InitializeAsync(f.Token);
            await using var composed = new Composition(f);
            await composed.Model.RefreshAsync();
            await composed.Model.SelectAsync(composed.Model.Sessions.Single());
            await composed.Model.ReadConversationAsync();
            composed.Model.ComposerBody = "original unsaved composer";
            var target = composed.Model.SelectedSessionRecord;
            using var model = new SavedDraftCatalogueViewModel(
                WindowsSqliteSessionWorkspaceTests.Service(f, composed.Access), composed.Model.SelectSavedDraftAsync,
                composed.Access, () => true, NullLogger<SavedDraftCatalogueViewModel>.Instance);
            var window = new SavedDraftCatalogueWindow(model);
            window.Show();
            try
            {
                var mode = window.FindControl<ComboBox>("DraftQueryMode")!;
                var query = window.FindControl<TextBox>("DraftQueryText")!;
                var search = window.FindControl<Button>("RefreshPage")!;
                mode.SelectedItem = SavedDraftCatalogueMode.NameSubstring;
                foreach (var invalid in new[]
                {
                    "", " ", " café", "café ", "cafe\u0301", "bad\nquery", "bad\u200Dquery",
                    "\uD800", new string('x', 121), string.Concat(Enumerable.Repeat("🙂", 121)),
                })
                {
                    query.Text = invalid;
                    model.QueryText.Should().Be(invalid);
                    model.CanSearch.Should().BeFalse();
                    search.IsEnabled.Should().BeFalse();
                    model.Records.Should().BeEmpty();
                }
                await model.Refresh();
                model.Status.Should().Contain("InvalidDataException");
                reads.Should().Be(0, "neither invalid input nor keystrokes perform I/O");
                query.Text = "café 🙂 100%_";
                search.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.Operation;
                var row = model.Records.Should().ContainSingle().Which;
                model.SelectedRecord.Should().BeNull("search never selects the first match");
                window.FindControl<ScrollableListBox>("DraftRecords")!.SelectedItem = row;
                model.CanNavigate.Should().BeTrue();
                query.Text = "Café";
                model.Records.Should().BeEmpty();
                model.SelectedRecord.Should().BeNull();
                model.CanNavigate.Should().BeFalse();
                reads.Should().Be(1);
                search.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.Operation;
                model.Records.Should().BeEmpty("name policy is ordinal case-sensitive");
                mode.SelectedItem = SavedDraftCatalogueMode.ExactId;
                foreach (var invalid in new[] { "", original.Value.ToString("D").ToUpperInvariant(),
                    " " + original.Value.ToString("D"), Guid.Empty.ToString("D"), original.Value.ToString("N") })
                {
                    query.Text = invalid;
                    model.CanSearch.Should().BeFalse();
                }
                query.Text = original.Value.ToString("D");
                search.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.Operation;
                model.Records.Should().ContainSingle().Which.Session.SessionId.Should().Be(original);
                model.Status.Should().NotContain("PRIVATE_NOT_A_QUERY_RESULT");
                window.FindControl<ComboBox>("DraftFilter")!.SelectedItem = SessionListFilter.Done;
                model.Records.Should().BeEmpty();
                model.SelectedRecord.Should().BeNull();
                search.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.Operation;
                model.Status.Should().Contain("not found").And.Contain("no fallback");
                composed.Model.ComposerBody.Should().Be("original unsaved composer");
                composed.Model.SelectedSessionRecord.Should().BeSameAs(target);
                window.FindControl<Button>("ClearQuery")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.Mode.Should().Be(SavedDraftCatalogueMode.Browse);
                model.QueryText.Should().BeEmpty();
                model.Filter.Should().Be(SessionListFilter.Done);
                model.Records.Should().BeEmpty("Clear is not an implicit read");
                model.Mode = SavedDraftCatalogueMode.NameSubstring;
                model.QueryText = "private volatile query";
                window.Close();
                model.QueryText.Should().BeEmpty();
                model.Status.Should().BeEmpty();
                model.Records.Should().BeEmpty();
                model.CanEditQuery.Should().BeFalse();
                composed.Model.ComposerBody.Should().Be("original unsaved composer");
                f.Count("conversation_messages").Should().Be(0);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("query")]
    [InlineData("mode")]
    [InlineData("filter")]
    [InlineData("revision")]
    [InlineData("minimum")]
    [InlineData("maximum")]
    [InlineData("scalar-minimum")]
    [InlineData("scalar-maximum")]
    [InlineData("source-field")]
    [InlineData("profile-field")]
    public async Task QueryEpochRevocationDuringExactDraftNavigationPreservesTheCurrentDirtyComposer(string boundary)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await WindowsSqliteConversationTests.Save(f,
            await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "private target body");
        var checkpoint = new CatalogueCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        await using var composed = new Composition(f);
        await composed.Model.RefreshAsync();
        await composed.Model.SelectAsync(composed.Model.Sessions.Single());
        await composed.Model.ReadConversationAsync();
        composed.Model.ComposerBody = "unchanged private dirty body";
        var original = composed.Model.SelectedSessionRecord;
        using var model = new SavedDraftCatalogueViewModel(WindowsSqliteSessionWorkspaceTests.Service(f, composed.Access),
            composed.Model.SelectSavedDraftAsync, composed.Access, () => true, NullLogger<SavedDraftCatalogueViewModel>.Instance);
        model.Mode = SavedDraftCatalogueMode.ExactId;
        model.QueryText = f.Request.SessionId.Value.ToString("D");
        await model.Refresh();
        model.Select(model.Records.Single());
        checkpoint.Read = () =>
        {
            checkpoint.Read = null;
            if (boundary is "query") { model.QueryText = Guid.NewGuid().ToString("D"); }
            else if (boundary is "mode") { model.Mode = SavedDraftCatalogueMode.NameSubstring; }
            else if (boundary is "revision") { model.RevisionText = "2"; }
            else if (boundary is "minimum") { model.MinimumBytesText = "1"; }
            else if (boundary is "maximum") { model.MaximumBytesText = "1"; }
            else if (boundary is "scalar-minimum") { model.MinimumScalarsText = "1"; }
            else if (boundary is "scalar-maximum") { model.MaximumScalarsText = "1"; }
            else if (boundary is "source-field") { model.SourceText = Guid.NewGuid().ToString("D"); }
            else if (boundary is "profile-field") { model.ProfileText = Guid.NewGuid().ToString("D"); }
            else { model.Filter = SessionListFilter.Done; }
        };
        await model.Navigate();
        model.Records.Should().BeEmpty();
        model.SelectedRecord.Should().BeNull();
        composed.Model.SelectedSessionRecord.Should().BeSameAs(original);
        composed.Model.ComposerBody.Should().Be("unchanged private dirty body");
        f.Count("conversation_messages").Should().Be(0);
    }

    [Theory]
    [InlineData("query")]
    [InlineData("mode")]
    [InlineData("filter")]
    [InlineData("source")]
    [InlineData("control")]
    [InlineData("cancel")]
    [InlineData("close")]
    [InlineData("revision")]
    [InlineData("minimum")]
    [InlineData("maximum")]
    [InlineData("scalar-minimum")]
    [InlineData("scalar-maximum")]
    [InlineData("private")]
    [InlineData("failure")]
    [InlineData("source-field")]
    [InlineData("profile-field")]
    public async Task DraftQueryChangesRevokePendingPagesAndKeepLateCallbacksFromRestoringPrivateResults(string boundary)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await WindowsSqliteConversationTests.Save(f,
            await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "saved body");
        var checkpoint = new CatalogueCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        using var model = new SavedDraftCatalogueViewModel(WindowsSqliteSessionWorkspaceTests.Service(f, access),
            (_, _, _) => Task.FromResult(false), access, () => true, NullLogger<SavedDraftCatalogueViewModel>.Instance);
        model.Mode = SavedDraftCatalogueMode.ExactId;
        model.QueryText = f.Request.SessionId.Value.ToString("D");
        model.RevisionText = "1";
        model.MinimumBytesText = "1";
        model.MaximumBytesText = "16384";
        model.MinimumScalarsText = "1";
        model.MaximumScalarsText = "4096";
        await model.Refresh();
        var observed = model.Records.Single();
        model.SourceText = observed.Source.ToString("D");
        model.ProfileText = observed.Profile.Value.ToString("D");
        checkpoint.Read = () =>
        {
            checkpoint.Read = null;
            switch (boundary)
            {
                case "query": model.QueryText = Guid.NewGuid().ToString("D"); break;
                case "mode": model.Mode = SavedDraftCatalogueMode.NameSubstring; break;
                case "filter": model.Filter = SessionListFilter.Done; break;
                case "revision": model.RevisionText = "2"; break;
                case "minimum": model.MinimumBytesText = "2"; break;
                case "maximum": model.MaximumBytesText = "2"; break;
                case "scalar-minimum": model.MinimumScalarsText = "2"; break;
                case "scalar-maximum": model.MaximumScalarsText = "2"; break;
                case "source-field": model.SourceText = Guid.NewGuid().ToString("D"); break;
                case "profile-field": model.ProfileText = Guid.NewGuid().ToString("D"); break;
                case "private": access.CanInspect = false; model.SynchronizeAuthority(); break;
                case "failure": throw new InvalidDataException("source failed");
                case "source": model.Revoke(); break;
                case "control": access.ControlRevision++; model.SynchronizeAuthority(); break;
                case "cancel": model.Cancel(); break;
                default: model.Dispose(); break;
            }
        };
        await model.Refresh();
        model.Records.Should().BeEmpty();
        model.SelectedRecord.Should().BeNull();
        model.CanNext.Should().BeFalse();
        model.CanNavigate.Should().BeFalse();
        if (boundary is "source" or "control" or "cancel" or "close") { model.QueryText.Should().BeEmpty(); }
        if (boundary is "close") { model.Status.Should().BeEmpty(); }
        if (boundary is "source" or "control" or "cancel" or "close" or "private")
        {
            model.RevisionText.Should().BeEmpty();
            model.MinimumBytesText.Should().BeEmpty();
            model.MaximumBytesText.Should().BeEmpty();
            model.MinimumScalarsText.Should().BeEmpty();
            model.MaximumScalarsText.Should().BeEmpty();
            model.SourceText.Should().BeEmpty();
            model.ProfileText.Should().BeEmpty();
        }
        f.Count("conversation_messages").Should().Be(0);
    }

    [Theory]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("cancel")]
    [InlineData("close")]
    [InlineData("selection")]
    [InlineData("source")]
    public async Task CatalogueLateNavigationCannotSwitchAfterOriginalPrivateSourceOrSelectionRevocation(string boundary)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var original = f.Request.SessionId;
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        await WindowsSqliteConversationTests.FinishInitial(f);
        await WindowsSqliteConversationTests.Save(f,
            await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "private target draft");
        var checkpoint = new CatalogueCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        await using var composed = new Composition(f);
        var model = composed.Model;
        await model.RefreshAsync();
        var other = model.Sessions.Single(row => row.Authority.SessionId != original);
        await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        var gate = true;
        using var viewer = HostActivity.BeginRoot(WindowsSqliteConversationTests.Fresh(original), HostActivityLayer.Desktop, HostOperation.Request);
        var exact = (await f.Store.ReadSavedDraftPage(SessionListFilter.All, null, 1, () => true, f.Token)).Records.Single();
        checkpoint.Read = () =>
        {
            checkpoint.Read = null;
            switch (boundary)
            {
                case "private": composed.Access.CanInspect = false; break;
                case "control": composed.Access.ControlRevision++; break;
                case "cancel": cancellation.Cancel(); break;
                case "close": model.Close(); break;
                case "selection": _ = model.SelectAsync(other); break;
                default: gate = false; break;
            }
        };
        var action = () => model.SelectSavedDraftAsync(exact, () => gate, cancellation.Token);
        await action.Should().ThrowAsync<Exception>();
        model.ComposerBody.Should().BeEmpty();
        model.ConversationMessages.Should().BeEmpty();
        if (boundary is not "close") { model.SelectedSessionRecord!.Authority.SessionId.Should().Be(original); }
        f.Count("conversation_messages").Should().Be(0);
    }

    private sealed class CatalogueCheckpoint : IHostInteractionTransactionCheckpoint
    {
        internal Action? Read { get; set; }
        public void BeforeAudit(SqliteConnection connection, SqliteTransaction transaction) { }
        public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction) { }
        public void BeforeConversationDraftMetadataRead(SqliteCommand command) => Read?.Invoke();
    }

    [Fact]
    public async Task ActualNativeCatalogueRefreshSelectionNavigationAndReloadAreSeparateExplicitActions()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var saved = await WindowsSqliteConversationTests.Save(f,
                await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "PRIVATE_CATALOGUE_BODY_43562");
            f.Request = WindowsSqliteConversationTests.Fresh(saved.Session.SessionId);
            await f.RunAsync(async () =>
            {
                await f.Store.RecordControlIntentAsync(f.Request, f.Token);
                await f.Store.ChangeIdleLifecycleAsync(f.Request, new(1), false, () => true, f.Token);
            });
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            var workspace = WindowsSqliteSessionWorkspaceTests.Service(f, composed.Access);
            var model = new SavedDraftCatalogueViewModel(workspace, composed.Model.SelectSavedDraftAsync,
                composed.Access, () => true, NullLogger<SavedDraftCatalogueViewModel>.Instance);
            var catalogue = new SavedDraftCatalogueWindow(model);
            var sessions = new SessionsWindow(composed.Model);
            catalogue.Show();
            sessions.Show();
            try
            {
                model.Records.Should().BeEmpty();
                catalogue.FindControl<Button>("RefreshPage")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await catalogue.Operation;
                catalogue.UpdateLayout();
                var exact = model.Records.Single();
                var list = catalogue.GetVisualDescendants().OfType<ScrollableListBox>().Single();
                list.SelectedItem = exact;
                ControlAutomationPeer.CreatePeerForElement(list)!.GetName().Should().Contain("no body preview");
                model.SelectedDetails.Should().Contain(saved.Session.SessionId.Value.ToString("D")).And.NotContain("PRIVATE_CATALOGUE_BODY");
                catalogue.FindControl<Button>("OpenExact")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await catalogue.Operation;
                composed.Model.SelectedSessionRecord!.Authority.Should().Be(exact.Session);
                composed.Model.ComposerBody.Should().BeEmpty();
                composed.Model.ConversationMessages.Should().BeEmpty();
                composed.Model.CanReloadConversationDraft.Should().BeTrue();
                composed.Model.CanCompose.Should().BeFalse("retained Done navigation is historical, not Resume");
                composed.Model.CanSubmitConversation.Should().BeFalse();
                composed.Model.ComposerTarget.Should().Contain("saved draft revision 1");
                sessions.FindControl<Button>("ReloadConversationDraft")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await sessions.ConversationOperation;
                composed.Model.ComposerBody.Should().Be(saved.Draft.Body);
                composed.Model.CanSubmitConversation.Should().BeFalse();
                f.Count("conversation_messages").Should().Be(0);
                model.Revoke();
                model.SelectedDetails.Should().BeEmpty();
                model.Records.Should().BeEmpty();
                catalogue.FindControl<Button>("CloseView")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.Status.Should().BeEmpty();
            }
            finally { catalogue.Close(); sessions.ClosePrivate(); }
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CatalogueNavigationProtectsOriginalDirtyBufferAndNeverOverwritesTarget(int choice)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var original = f.Request.SessionId;
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        await WindowsSqliteConversationTests.FinishInitial(f);
        var saved = await WindowsSqliteConversationTests.Save(f,
            await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "target saved body");
        await using var composed = new Composition(f);
        await composed.Model.RefreshAsync();
        await composed.Model.SelectAsync(composed.Model.Sessions.Single(row => row.Authority.SessionId == original));
        await composed.Model.ReadConversationAsync();
        composed.Model.ComposerBody = "original dirty body";
        composed.Model.DecideComposerDraft = () => Task.FromResult((ComposerDraftDecision)choice);
        using var viewer = HostActivity.BeginRoot(WindowsSqliteConversationTests.Fresh(original), HostActivityLayer.Desktop, HostOperation.Request);
        var exact = (await f.Store.ReadSavedDraftPage(SessionListFilter.All, null, 1, () => true, f.Token)).Records.Single();
        if ((ComposerDraftDecision)choice == ComposerDraftDecision.Save)
        {
            var changed = () => composed.Model.SelectSavedDraftAsync(exact, () => true, f.Token);
            await changed.Should().ThrowAsync<InvalidOperationException>("the explicit dirty-buffer save changes the conservative authority head; Refresh is required");
        }
        else
        {
            var opened = await composed.Model.SelectSavedDraftAsync(exact, () => true, f.Token);
            opened.Should().Be((ComposerDraftDecision)choice == ComposerDraftDecision.Discard);
        }
        var cancel = (ComposerDraftDecision)choice == ComposerDraftDecision.Cancel;
        composed.Model.SelectedSessionRecord!.Authority.SessionId.Should().Be(
            (ComposerDraftDecision)choice == ComposerDraftDecision.Discard ? saved.Session.SessionId : original);
        composed.Model.ComposerBody.Should().Be(cancel || (ComposerDraftDecision)choice == ComposerDraftDecision.Save
            ? "original dirty body" : string.Empty);
        (await f.Store.ReadConversationDraftAsync(saved.Session.SessionId, f.Token)).Should().Be(saved);
        (await f.Store.ReadConversationDraftAsync(original, f.Token)).Draft.Body.Should().Be(
            (ComposerDraftDecision)choice == ComposerDraftDecision.Save ? "original dirty body" : null);
        f.Count("conversation_messages").Should().Be(0);
    }
}
