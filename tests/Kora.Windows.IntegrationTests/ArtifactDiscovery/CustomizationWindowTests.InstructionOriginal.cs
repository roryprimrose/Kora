using System.Security.AccessControl;
using System.Security.Principal;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Core.Artifacts;
using Kora.Core.Communication;
using Kora.Core.Customizations;
using Kora.Definitions.Artifacts;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class CustomizationWindowTests
{
    [Theory]
    [InlineData("complete")]
    [InlineData("privacy")]
    [InlineData("withdraw")]
    [InlineData("owner")]
    [InlineData("call")]
    public Task PersonalOriginalRequiresRegistrationAndSeparateSaveWithDirtyConflictRecovery(string state) => HeadlessSession.RunAsync(async () =>
    {
        var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
        Directory.CreateDirectory(parent);
        using var f = new NativeUxFixtureSession(parent);
        var calls = new OriginalCallState();
        await f.InitializeAsync(null, callState: calls);
        await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
        var profile = Path.Combine(f.LocalRoot, "personal-editor-profile");
        var directory = Path.Combine(profile, "Code", "User", "prompts");
        var userDirectory = Path.GetDirectoryName(directory)!;
        Directory.CreateDirectory(Path.GetDirectoryName(userDirectory)!);
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User ?? throw new InvalidOperationException("The private fixture owner is unavailable.");
        RestrictedStorageDirectory.CreateRestrictedDirectory(userDirectory, owner);
        RestrictedStorageDirectory.CreateRestrictedDirectory(directory, owner);
        var path = Path.Combine(directory, "example.instructions.md");
        var original = CustomizationLayout.Template(CustomizationKind.Instruction, "example").Replace("\n", "\r\n", StringComparison.Ordinal);
        WindowsOwnedInstructionStoreTests.WriteOwnedFile(path, ArtifactFileDocument.Encode(original, true));
        // Inventory also pins the editor User directory as an MCP root.
        foreach (var inventoryDirectory in new[] { userDirectory, directory })
        {
            var security = new DirectoryInfo(inventoryDirectory).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            security.GetOwner(typeof(SecurityIdentifier)).Should().Be(owner);
            security.AreAccessRulesProtected.Should().BeTrue();
        }
        new FileInfo(path).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier))
            .Should().Be(owner);
        var store = new WindowsCustomizationStore(f, profile, Path.Combine(f.LocalRoot, "personal-user-profile"));
        var use = new ArtifactUsePreferences(f);
        using var catalogue = EmbeddedArtifactCatalogue.Load();
        var management = new CustomizationManagementService(store, admission, use, catalogue, f.Audit);
        var directories = new LocalArtifactDirectoryPreferences(f);
        using var reload = new ArtifactCatalogueReloadService(catalogue, f, directories,
            new ArtifactDirectoryConfigurationService(directories, f.Audit), new McpServerConfigurationService(f, directories, profile),
            NullLogger<ArtifactCatalogueReloadService>.Instance, profileRoamingRoot: profile);
        var authoring = new Authoring();
        var ownsHost = true;
        long ownerRevision = 1;
        using var controller = new CustomizationWindowController(f.Main, management, authoring, catalogue, reload,
            () => ownsHost, () => ownerRevision, NullLogger<CustomizationWindowController>.Instance);
        CustomizationWindow? editor = null;
        var next = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((view, _) =>
        {
            if (view is CustomizationWindow selected) { editor = selected; }
            else { next.TrySetResult(view); }
        });
        void Choose(Window view, string caption) => view.GetVisualDescendants().OfType<Button>()
            .Single(button => Equals(button.Content, caption)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task<Window> BeginReview(string caption)
        {
            next = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Choose(editor!, caption);
            return await next.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token);
        }
        controller.Bind();
        f.Main.CommandText = "edit example instruction";
        await f.Main.RunTypedCommand.ExecuteAsync();
        editor.Should().NotBeNull();
        editor!.Editor.IsReadOnly.Should().BeTrue();
        editor.Editor.Text.Should().BeEmpty();
        editor.Status.Text.Should().Contain("No original content has been read");
        var selectedSource = (await store.List(f.Token)).Single(source => source.Kind == CustomizationKind.Instruction && source.Scope == CustomizationScope.User);
        var registration = await BeginReview("Review original registration");
        registration.GetVisualDescendants().OfType<TextBox>().Single().Text.Should().Contain(path).And.Contain("does NOT approve a write");
        Choose(registration, "Cancel");
        await controller.WaitForOperation();
        store.IsInstructionRegistered(selectedSource).Should().BeFalse();
        editor.Editor.Text.Should().BeEmpty();
        registration = await BeginReview("Review original registration");
        Choose(registration, "Register exact original");
        await controller.WaitForOperation();
        editor.Editor.IsReadOnly.Should().BeFalse();
        editor.Editor.Text.Should().Be(original);
        var proposed = original + "\r\nExact retained draft.";
        editor.Editor.Text = proposed;
        var saveReview = await BeginReview("Review and save");
        saveReview.GetVisualDescendants().OfType<TextBox>().Single().Text.Should().Contain(path).And.Contain(original).And.Contain(proposed);
        if (state is "privacy" or "owner" or "call")
        {
            if (state is "owner")
            {
                ownsHost = false; ownerRevision++;
                Choose(saveReview, "Save disabled");
                await controller.WaitForOperation();
                (await File.ReadAllBytesAsync(path, f.Token)).Should().Equal(ArtifactFileDocument.Encode(original, true));
                controller.Dispose();
            }
            else if (state is "call") { calls.Set(CallState.Active); }
            else { f.Main.CloseForObservedPrivacyEvent("Synthetic profile-original privacy closure", hidePresentation: true); }
            await f.Main.PrivacyClosureTask;
            await controller.WaitForOperation();
            editor.Editor.Text.Should().BeEmpty();
            editor.IsVisible.Should().BeFalse();
            store.IsInstructionRegistered(selectedSource).Should().BeFalse();
            (await File.ReadAllBytesAsync(path, f.Token)).Should().Equal(ArtifactFileDocument.Encode(original, true));
            return;
        }
        Choose(saveReview, "Cancel");
        await controller.WaitForOperation();
        editor.Editor.Text.Should().Be(proposed);
        (await File.ReadAllBytesAsync(path, f.Token)).Should().Equal(ArtifactFileDocument.Encode(original, true));
        if (state is "withdraw")
        {
            var withdrawal = await BeginReview("Withdraw original registration");
            Choose(withdrawal, "Withdraw and clear");
            await controller.WaitForOperation();
            store.IsInstructionRegistered(selectedSource).Should().BeFalse();
            editor.Editor.Text.Should().BeEmpty();
            editor.Editor.IsReadOnly.Should().BeTrue();
            (await File.ReadAllBytesAsync(path, f.Token)).Should().Equal(ArtifactFileDocument.Encode(original, true));
        }
        else
        {
            var external = original + "\r\nExternal source.";
            await File.WriteAllBytesAsync(path, ArtifactFileDocument.Encode(external, true), f.Token);
            saveReview = await BeginReview("Review and save");
            Choose(saveReview, "Save disabled");
            await controller.WaitForOperation();
            editor.Editor.Text.Should().Be(proposed);
            editor.Status.Text.Should().Contain("changed");
            (await File.ReadAllBytesAsync(path, f.Token)).Should().Equal(ArtifactFileDocument.Encode(external, true));
            var comparison = await BeginReview("Compare external");
            comparison.GetVisualDescendants().OfType<TextBox>().Single().Text.Should().Contain(external).And.Contain(proposed);
            Choose(comparison, "Keep draft");
            await controller.WaitForOperation();
            editor.Editor.Text.Should().Be(proposed);
            comparison = await BeginReview("Compare external");
            await File.WriteAllBytesAsync(path, ArtifactFileDocument.Encode(external + "\r\nChanged again.", true), f.Token);
            Choose(comparison, "Rebase complete draft");
            await controller.WaitForOperation();
            editor.Status.Text.Should().Contain("changed again");
            editor.Editor.Text.Should().Be(proposed);
            comparison = await BeginReview("Compare external");
            Choose(comparison, "Rebase complete draft");
            await controller.WaitForOperation();
            editor.Status.Text.Should().Contain("Nothing written or enabled");
            saveReview = await BeginReview("Review and save");
            Choose(saveReview, "Save disabled");
            await controller.WaitForOperation();
            editor.Status.Text.Should().Contain("Saved and read back");
            (await File.ReadAllBytesAsync(path, f.Token)).Should().Equal(ArtifactFileDocument.Encode(proposed, true));
            use.IsDisabled(selectedSource.Id).Should().BeTrue();
            var enable = await BeginReview("Enable");
            enable.GetVisualDescendants().OfType<TextBox>().Single().Text.Should().Contain(proposed);
            Choose(enable, "Cancel");
            await controller.WaitForOperation();
            use.IsDisabled(selectedSource.Id).Should().BeTrue();
            editor.Editor.Text += "\r\nUnsent.";
            next = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var close = controller.PrepareClose();
            var dirty = await next.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token);
            Choose(dirty, "Cancel");
            (await close).Should().BeFalse();
            editor.Editor.Text.Should().EndWith("Unsent.");
        }
        authoring.Calls.Should().Be(0);
        authoring.InterpretCalls.Should().Be(0);
    });

    private sealed class OriginalCallState : ICallStateService
    {
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
        public CallState CurrentState { get; private set; } = CallState.Clear;
        internal void Set(CallState state)
        {
            CurrentState = state;
            StateChanged?.Invoke(this, new(state));
        }
    }
}