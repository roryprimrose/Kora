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
    [Fact]
    public async Task GenerationPickerIsVisibleAccessibleNonNullAnyExplicitAndRetiresSelectionAndLatePagesWithoutReads()
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
            var picker = window.FindControl<ComboBox>("AttachmentGenerationRelationship")!;
            try
            {
                picker.IsVisible.Should().BeTrue();
                picker.SelectedItem.Should().NotBeNull().And.Be(model.GenerationRelationships[0]);
                model.GenerationRelationships.Select(choice => choice.ToString()).Should()
                    .Equal("Any", "Same observed generation", "Earlier observed generation");
                ControlAutomationPeer.CreatePeerForElement(picker)!.GetName().Should().Contain("passive historical metadata")
                    .And.Contain("performs no read");
                picker.Focus();
                picker.IsFocused.Should().BeTrue();
                seen.Should().BeEmpty();
                await model.Refresh();
                model.Select(summary);
                picker.SelectedItem = model.GenerationRelationships[1];
                model.Records.Should().BeEmpty();
                model.SelectedDetails.Should().BeEmpty();
                model.CanNavigate.Should().BeFalse();
                model.CanNext.Should().BeFalse();
                revoked.Should().Be(1);
                seen.Should().HaveCount(1);
                foreach (var choice in model.GenerationRelationships)
                {
                    picker.SelectedItem = choice;
                    var reads = seen.Count;
                    picker.SelectedItem = choice;
                    seen.Should().HaveCount(reads);
                    await model.Refresh();
                    seen.Last().GenerationRelationship.Should().Be(choice.Relationship);
                    await model.Refresh(next: true);
                }
                model.ClearQuery();
                picker.SelectedItem.Should().Be(model.GenerationRelationships[0]);
                await model.Refresh();
                model.Select(summary);
                pending = new();
                var read = model.Refresh();
                var original = seen.Last();
                picker.SelectedItem = model.GenerationRelationships[2];
                observedToken.IsCancellationRequested.Should().BeTrue();
                current!().Should().BeFalse();
                pending.SetResult(new(original, [summary], null, DateTimeOffset.UnixEpoch, 1, new string('b', 64), 1, false));
                await read;
                pending = null;
                model.Records.Should().BeEmpty();
                model.CanNext.Should().BeFalse();
                foreach (var invalid in new AttachmentGenerationRelationshipChoice?[] { null, new((AttachmentGenerationRelationship)99) })
                {
                    model.GenerationRelationship = invalid;
                    var reads = seen.Count;
                    model.CanSearch.Should().BeFalse();
                    model.GenerationRelationshipHint.Should().Contain("Invalid").And.Contain("Choose Any");
                    await model.Refresh();
                    model.Status.Should().Contain("InvalidDataException");
                    seen.Should().HaveCount(reads);
                }
                model.ClearQuery();
                picker.SelectedItem.Should().Be(model.GenerationRelationships[0]);
                picker.SelectedItem = model.GenerationRelationships[1];
                model.Cancel();
                picker.SelectedItem.Should().Be(model.GenerationRelationships[0]);
                picker.SelectedItem = model.GenerationRelationships[2];
                picker.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                window.IsVisible.Should().BeFalse();
                model.GenerationRelationship.Should().Be(model.GenerationRelationships[0]);
                model.GenerationRelationship = model.GenerationRelationships[2];
                model.GenerationRelationship.Should().Be(model.GenerationRelationships[0]);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("ownership")]
    [InlineData("source")]
    [InlineData("dispose")]
    [InlineData("edit")]
    [InlineData("cancel")]
    [InlineData("clear")]
    public async Task GenerationQueryRevocationCancelsNavigationAndSuppressesLateCompletion(string reason)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var summary = Summary(HostRequest.Create(RequestOrigin.LocalUi));
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var pending = new TaskCompletionSource();
            Func<bool>? current = null;
            CancellationToken observed = default;
            using var model = new AttachmentCatalogueViewModel((query, _, _, _) => Task.FromResult(
                new SessionAttachmentPage(query, [summary], null, DateTimeOffset.UnixEpoch, 1, new string('b', 64), 1, false)),
                (_, admitted, token) => { current = admitted; observed = token; return pending.Task; },
                access, () => true, new CatalogueLogger());
            model.GenerationRelationship = model.GenerationRelationships[1];
            await model.Refresh();
            model.Select(summary);
            var navigation = model.Navigate();
            switch (reason)
            {
                case "privacy": access.CanInspect = false; model.SynchronizeAuthority(); break;
                case "ownership": access.ControlRevision++; model.SynchronizeAuthority(); break;
                case "source": model.Revoke(); break;
                case "edit": model.GenerationRelationship = model.GenerationRelationships[0]; break;
                case "cancel": model.Cancel(); break;
                case "clear": model.ClearQuery(); break;
                default: model.Dispose(); break;
            }
            observed.IsCancellationRequested.Should().BeTrue();
            current!().Should().BeFalse();
            model.GenerationRelationship.Should().Be(model.GenerationRelationships[0]);
            model.SelectedDetails.Should().BeEmpty();
            model.CanNavigate.Should().BeFalse();
            model.CanNext.Should().BeFalse();
            pending.SetResult();
            await navigation;
            model.Status.Should().NotContain("Opened");
        });
    }
}
