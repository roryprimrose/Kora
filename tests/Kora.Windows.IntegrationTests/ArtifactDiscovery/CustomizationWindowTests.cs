using System.Text;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Core.Customizations;
using Kora.Definitions.Artifacts;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed partial class CustomizationWindowTests
{
    [Fact]
    public Task TrayCustomizationEntryReusesTheExistingGuardedManagementWindow() => HeadlessSession.RunAsync(async () =>
    {
        using var f = new NativeUxFixtureSession(Path.GetTempPath());
        await f.InitializeAsync();
        await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
        var profile = Path.Combine(f.LocalRoot, "tray-customizations");
        var store = new WindowsCustomizationStore(f, profile, Path.Combine(f.LocalRoot, "tray-user"));
        var use = new ArtifactUsePreferences(f);
        using var catalogue = EmbeddedArtifactCatalogue.Load();
        var management = new CustomizationManagementService(store, admission, use, catalogue, f.Audit);
        var directories = new LocalArtifactDirectoryPreferences(f);
        using var reload = new ArtifactCatalogueReloadService(catalogue, f, directories,
            new ArtifactDirectoryConfigurationService(directories, f.Audit),
            new McpServerConfigurationService(f, directories, profile),
            NullLogger<ArtifactCatalogueReloadService>.Instance, profileRoamingRoot: profile);
        var authoring = new Authoring();
        var ownsHost = true;
        using var controller = new CustomizationWindowController(f.Main, management, authoring, catalogue, reload,
            () => ownsHost, () => 1, NullLogger<CustomizationWindowController>.Instance);
        controller.Bind();
        var windows = new List<CustomizationWindow>();
        using var observe = Window.WindowOpenedEvent.AddClassHandler<CustomizationWindow>((view, _) => windows.Add(view));
        var navigation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var tray = new SystemTrayController(f.Main, NullLogger<SystemTrayController>.Instance,
            manageCustomizations: () => { controller.Open(); navigation.SetResult(); });
        var item = TrayIcon.GetIcons(Avalonia.Application.Current!)!.Single().Menu!.Items.OfType<NativeMenuItem>()
            .Single(item => string.Equals(item.Header, "Kora Customizations", StringComparison.Ordinal));
        for (var i = 0; i < 2; i++)
        {
            ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();
            await navigation.Task.WaitAsync(f.Token);
            await controller.WaitForOperation();
            windows.Should().ContainSingle();
            windows[0].IsVisible.Should().BeTrue();
            windows[0].Title.Should().Be("Kora Customizations");
            navigation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        ownsHost = false;
        ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();
        await navigation.Task.WaitAsync(f.Token);
        windows.Should().ContainSingle();
        f.Main.ResponseBody.Should().Contain("Customizations requires the current unlocked owning host");
        authoring.Calls.Should().Be(0);
        authoring.InterpretCalls.Should().Be(0);
        f.Main.IsVoiceEnabled.Should().BeFalse();
    });

    [Theory]
    [InlineData("edit")]
    [InlineData("clarify")]
    [InlineData("clarify-discuss")]
    [InlineData("clarify-cancel")]
    [InlineData("discuss")]
    [InlineData("cancel")]
    [InlineData("request-change")]
    [InlineData("component-change")]
    [InlineData("hidden-presentation")]
    [InlineData("discussion-change")]
    [InlineData("late-privacy")]
    [InlineData("built-in")]
    [InlineData("saved-disabled")]
    [InlineData("selected-component")]
    public async Task Foreground_voice_handoffs_review_metadata_then_complete_draft_and_never_save_or_retarget(string state)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            f.Main.BindVoiceOwnershipGate(static () => true);
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
            var store = new WindowsCustomizationStore(f, Path.Combine(f.LocalRoot, "test-editor"), Path.Combine(f.LocalRoot, "test-user"));
            var use = new ArtifactUsePreferences(f);
            using var catalogue = EmbeddedArtifactCatalogue.Load();
            var management = new CustomizationManagementService(store, admission, use, catalogue, f.Audit);
            var directories = new LocalArtifactDirectoryPreferences(f);
            using var reload = new ArtifactCatalogueReloadService(catalogue, f, directories,
                new ArtifactDirectoryConfigurationService(directories, f.Audit),
                new McpServerConfigurationService(f, directories, Path.Combine(f.LocalRoot, "test-editor")),
                NullLogger<ArtifactCatalogueReloadService>.Instance, profileRoamingRoot: Path.Combine(f.LocalRoot, "test-editor"));
            var authoring = new Authoring
            {
                Intent = state is "clarify" or "clarify-cancel" or "clarify-discuss" ? CustomizationVoiceIntent.Clarify
                    : state is "discussion-change" or "discuss" ? CustomizationVoiceIntent.Discuss : CustomizationVoiceIntent.Edit,
                PreserveFiles = state is "discuss" or "clarify-discuss",
            };
            using var controller = new CustomizationWindowController(f.Main, management, authoring, catalogue, reload,
                () => true, () => 1, NullLogger<CustomizationWindowController>.Instance);
            CustomizationWindow? editor = null;
            var next = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((view, _) =>
            {
                if (view is CustomizationWindow selected) { editor = selected; }
                else if (view.Title?.StartsWith("Review", StringComparison.Ordinal) == true || view.Title is "Choose voice target") { next.TrySetResult(view); }
            });
            controller.Bind();
            f.Main.CommandText = state is "built-in"
                ? "edit " + Kora.Definitions.Skills.EmbeddedSkillCatalogue.Load().Packages.First().Manifest.Name + " skill"
                : state is "selected-component" ? "add a skill named example" : "add an instruction named example";
            await f.Main.RunTypedCommand.ExecuteAsync();
            editor.Should().NotBeNull();
            void Choose(Window view, string caption) => view.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, caption)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (state is "selected-component")
            {
                editor!.FileName.Text = @"references\notes.md";
                Choose(editor, "Add file");
                await controller.WaitForOperation();
                editor.Files.SelectedItem.Should().Be(@"references\notes.md");
                editor.Editor.Text = "An old example in this component.";
            }
            var path = Path.Combine(f.RoamingRoot, "Instructions", "example.instructions.md");
            string? savedOriginal = null;
            if (state is "saved-disabled")
            {
                editor!.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Review and save"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Choose(await next.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken), "Save disabled");
                await controller.WaitForOperation();
                savedOriginal = File.ReadAllText(path);
                management.IsDisabled((await store.List(TestContext.Current.CancellationToken)).Single()).Should().BeTrue();
                next = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            editor!.Activate();
            var target = controller.VoiceTarget;
            target.Should().NotBeNull(editor.VoiceContext.Text);
            target!.IsCurrent.Should().BeTrue();
            editor.VoiceContext.Text.Should().Contain(target.Context.Name);
            var original = editor.Editor.Text;
            var operation = controller.RouteVoice(target, "make this clearer", () => true);
            if (state is "built-in")
            {
                await operation;
                authoring.InterpretCalls.Should().Be(0);
                authoring.Calls.Should().Be(0);
                editor.Editor.Text.Should().Be(original);
                editor.Editor.IsReadOnly.Should().BeTrue();
                editor.Status.Text.Should().Contain("Built-ins require");
                return;
            }
            var routing = await next.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            authoring.InterpretCalls.Should().Be(0);
            authoring.Calls.Should().Be(0);
            if (state is "cancel" or "request-change" or "component-change" or "hidden-presentation")
            {
                if (state is "request-change") { editor.Request.Text = "Independent newer request"; }
                else if (state is "component-change")
                {
                    var component = editor.Files.SelectedItem;
                    editor.Files.SelectedItem = null;
                    editor.Files.SelectedItem = component;
                    target.IsCurrent.Should().BeFalse("selecting the same component again cannot revive capture");
                }
                else if (state is "hidden-presentation")
                {
                    editor.Hide();
                    editor.Show();
                    editor.Activate();
                    target.IsCurrent.Should().BeFalse("presentation reacquisition cannot revive the original capture");
                }
                else { Choose(routing, "Cancel"); }
                await operation.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                authoring.InterpretCalls.Should().Be(0);
                authoring.Calls.Should().Be(0);
                editor.Editor.Text.Should().Be(original);
            }
            else
            {
                next = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Choose(routing, "Interpret editor intent");
                var review = await next.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                authoring.InterpretCalls.Should().Be(1);
                if (state is "clarify" or "clarify-cancel" or "clarify-discuss")
                {
                    review.Title.Should().Be("Choose voice target");
                    if (state is "clarify-cancel")
                    {
                        Choose(review, "Cancel");
                        await operation.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                        authoring.Calls.Should().Be(0);
                        editor.Editor.Text.Should().Be(original);
                        File.Exists(path).Should().BeFalse();
                        return;
                    }
                    next = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    Choose(review, state is "clarify-discuss" ? "Discuss this artifact" : "Refine this artifact");
                    review = await next.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                }
                review.Title.Should().Be("Review model authoring disclosure");
                var disclosed = review.GetVisualDescendants().OfType<TextBox>().Single().Text;
                disclosed.Should().Contain(target.Context.Component!);
                disclosed.Should().Contain(state is "discuss" or "clarify-discuss" or "discussion-change"
                    ? "Discussion only; files unchanged" : "Edit proposal");
                authoring.Calls.Should().Be(0);
                next = new(TaskCreationOptions.RunContinuationsAsynchronously);
                if (state is "late-privacy")
                {
                    authoring.Pause = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    authoring.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                Choose(review, "Send for local authoring");
                if (state is "late-privacy")
                {
                    await authoring.Started!.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                    f.Main.CloseForObservedPrivacyEvent("Synthetic privacy closure", hidePresentation: true);
                    await f.Main.PrivacyClosureTask;
                    authoring.Pause!.SetResult();
                    await operation.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                    editor.Editor.Text.Should().BeEmpty();
                    editor.Request.Text.Should().BeEmpty();
                    target.IsCurrent.Should().BeFalse();
                }
                else if (state is "discussion-change")
                {
                    await operation.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                    next.Task.IsCompleted.Should().BeFalse();
                    editor.Editor.Text.Should().Be(original);
                    editor.Status.Text.Should().Contain("Discussion returned changed files");
                }
                else
                {
                    var proposal = await next.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                    Choose(proposal, "Use proposed draft");
                    await operation.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                    if (state is "discuss" or "clarify-discuss") { editor.Editor.Text.Should().Be(original); }
                    else { editor.Editor.Text.Should().Contain("Model proposal."); }
                    editor.Request.Text.Should().Be("make this clearer");
                }
                authoring.Context.Should().Be(new CustomizationAuthoringContext(target.Context.Component,
                    state is "discuss" or "clarify-discuss" or "discussion-change"
                        ? CustomizationAuthoringMode.Discuss : CustomizationAuthoringMode.Edit));
            }
            if (savedOriginal is null) { File.Exists(path).Should().BeFalse(); }
            else
            {
                File.ReadAllText(path).Should().Be(savedOriginal);
                management.IsDisabled((await store.List(TestContext.Current.CancellationToken)).Single()).Should().BeTrue();
            }
            controller.Dispose();
        });
    }

    private static async Task ActivateEditor(CustomizationWindow editor)
    {
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
        {
            if (change.Property == Window.IsActiveProperty && editor.IsActive) { activated.TrySetResult(); }
        }
        editor.PropertyChanged += OnChanged;
        try
        {
            editor.Activate();
            if (!editor.IsActive)
            { await activated.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken); }
        }
        finally { editor.PropertyChanged -= OnChanged; }
    }

    [Theory]
    [InlineData("release")]
    [InlineData("pointer")]
    [InlineData("focus-loss")]
    [InlineData("hide")]
    [InlineData("close")]
    [InlineData("response-close")]
    [InlineData("no-consent")]
    [InlineData("read-only")]
    public async Task Editor_PTT_control_owns_capture_and_routes_only_its_original_foreground_target(string state)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent, syntheticVoice: true);
            await f.InitializeAsync();
            f.Main.BindVoiceOwnershipGate(static () => true);
            if (state is not "no-consent") { await f.Main.SetVoiceConsentAsync(true); }
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
            var store = new WindowsCustomizationStore(f, Path.Combine(f.LocalRoot, "test-editor"), Path.Combine(f.LocalRoot, "test-user"));
            using var catalogue = EmbeddedArtifactCatalogue.Load();
            var management = new CustomizationManagementService(store, admission, new ArtifactUsePreferences(f), catalogue, f.Audit);
            var directories = new LocalArtifactDirectoryPreferences(f);
            using var reload = new ArtifactCatalogueReloadService(catalogue, f, directories,
                new ArtifactDirectoryConfigurationService(directories, f.Audit),
                new McpServerConfigurationService(f, directories, Path.Combine(f.LocalRoot, "test-editor")),
                NullLogger<ArtifactCatalogueReloadService>.Instance, profileRoamingRoot: Path.Combine(f.LocalRoot, "test-editor"));
            var authoring = new Authoring();
            using var controller = new CustomizationWindowController(f.Main, management, authoring, catalogue, reload,
                () => true, () => 1, NullLogger<CustomizationWindowController>.Instance);
            CustomizationWindow? editor = null;
            var routing = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((view, _) =>
            {
                if (view is CustomizationWindow selected) { editor = selected; }
                else if (view.Title is "Review editor voice routing disclosure") { routing.TrySetResult(view); }
            });
            var seed = await store.Create(CustomizationKind.Instruction, CustomizationScope.Kora, "example", f.Token);
            var initial = CustomizationLayout.Template(CustomizationKind.Instruction, "example");
            await store.Save(seed, null, [new(seed.PrimaryFile, Encoding.UTF8.GetBytes(initial))],
                () => true, operation => Task.FromResult(operation()), f.Token);
            var path = seed.Location;
            var response = new ResponseWindow(f.Main);
            try
            {
                response.Show();
                controller.Bind();
                f.Main.CommandText = state is "read-only"
                    ? "edit " + Kora.Definitions.Skills.EmbeddedSkillCatalogue.Load().Packages.First().Manifest.Name + " skill"
                    : "edit example instruction";
                await f.Main.RunTypedCommand.ExecuteAsync();
                editor.Should().NotBeNull();
                await ActivateEditor(editor!);
                editor.IsActive.Should().BeTrue();
                editor.UpdateLayout();
                if (state is not "read-only") { editor.Editor.Text.Should().Be(initial, editor.Status.Text); }
                if (state is "no-consent" or "read-only")
                {
                    editor.PushToTalkButton.IsEnabled.Should().BeFalse();
                    editor.PushToTalkButton.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
                    f.Voice.StartCalls.Should().Be(0);
                    f.Main.IsListening.Should().BeFalse();
                    authoring.InterpretCalls.Should().Be(0);
                    return;
                }
                editor.PushToTalkButton.IsEnabled.Should().BeTrue(
                    $"{editor.Status.Text} / {f.Main.ListeningStatus}; active={editor.IsActive}, busy={f.Main.IsBusy}, "
                    + $"canCapture={f.Main.CanPushToTalk}, currentTarget={controller.VoiceTarget?.IsCurrent}");
                editor.PushToTalkButton.Focus().Should().BeTrue();
                editor.PushToTalkButton.IsFocused.Should().BeTrue();
                f.Main.BeginPushToTalkCommand.CanExecute(null).Should().BeTrue();
                var target = controller.VoiceTarget;
                target.Should().NotBeNull();
                var point = editor.PushToTalkButton.TranslatePoint(new Point(2, 2), editor)!.Value;
                var gestureSeen = false;
                editor.PushToTalkButton.AddHandler(InputElement.PointerPressedEvent, (_, _) => gestureSeen = true,
                    RoutingStrategies.Tunnel, handledEventsToo: true);
                editor.PushToTalkButton.AddHandler(InputElement.KeyDownEvent, (_, _) => gestureSeen = true,
                    RoutingStrategies.Tunnel, handledEventsToo: true);
                if (state is "pointer") { editor.MouseDown(point, MouseButton.Left); }
                else { editor.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); }
                gestureSeen.Should().BeTrue("the simulated gesture must reach the native control");
                f.Voice.StartCalls.Should().Be(1, $"capture must open: {f.Main.ResponseTitle} / {f.Main.ResponseBody} / {f.Main.ListeningStatus}");
                await f.Voice.Started.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                f.Voice.StartCalls.Should().Be(1);
                f.Main.IsListening.Should().BeTrue();
                editor.IsActive.Should().BeTrue();
                controller.VoiceTarget.Should().BeSameAs(target);
                target!.IsCurrent.Should().BeTrue();
                editor.PushToTalkButton.IsEnabled.Should().BeTrue("the held control must still receive release");
                if (state is "response-close")
                {
                    response.Close();
                    f.Voice.EndCalls.Should().Be(0, "another window cannot release the editor's capture");
                }
                if (state is "pointer") { editor.MouseUp(point, MouseButton.Left); }
                else if (state is "focus-loss") { editor.Request.Focus(); }
                else if (state is "hide") { editor.Hide(); }
                else if (state is "close") { editor.Close(); }
                else { editor.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); }
                f.Voice.EndCalls.Should().Be(1);
                f.Voice.IsCaptureQuiescent.Should().BeTrue();
                f.Main.IsListening.Should().BeFalse();
                var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                f.Main.PropertyChanged += (_, args) =>
                {
                    if (state is "hide" or "close")
                    {
                        if (args.PropertyName is nameof(f.Main.ResponseBody)
                            && f.Main.ResponseBody.Contains("No session received", StringComparison.Ordinal))
                        { completed.TrySetResult(); }
                    }
                    else if (args.PropertyName is nameof(f.Main.IsBusy) && !f.Main.IsBusy) { completed.TrySetResult(); }
                };
                f.Voice.PublishTranscript("make this clearer");
                if (state is "hide" or "close")
                {
                    await completed.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                    routing.Task.IsCompleted.Should().BeFalse();
                    target.IsCurrent.Should().BeFalse();
                }
                else
                {
                    var review = await routing.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                    review.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Cancel"))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await completed.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                }
                authoring.InterpretCalls.Should().Be(0);
                authoring.Calls.Should().Be(0);
                File.ReadAllText(path).Should().Be(CustomizationLayout.Template(CustomizationKind.Instruction, "example"));
            }
            finally { response.Close(); }
        });
    }

    [Theory]
    [InlineData("keyboard")]
    [InlineData("enter")]
    [InlineData("pointer")]
    [InlineData("focus-loss")]
    [InlineData("hide")]
    [InlineData("close")]
    public async Task Customization_shared_PTT_binding_preserves_response_window_capture_and_release(string state)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent, syntheticVoice: true);
            await f.InitializeAsync();
            f.Main.BindVoiceOwnershipGate(static () => true);
            await f.Main.SetVoiceConsentAsync(true);
            await f.Main.ToggleListeningCommand.ExecuteAsync();
            f.Main.IsVoiceEnabled.Should().BeFalse();
            var response = new ResponseWindow(f.Main);
            try
            {
                response.Show();
                response.Activate();
                response.UpdateLayout();
                var button = response.FindControl<Button>("PushToTalkButton")!;
                button.Focus();
                button.IsFocused.Should().BeTrue();
                button.IsVisible.Should().BeTrue();
                button.IsEnabled.Should().BeTrue(f.Main.ListeningStatus);
                var key = state is "enter" ? Key.Enter : Key.Space;
                var physical = state is "enter" ? PhysicalKey.Enter : PhysicalKey.Space;
                var symbol = state is "enter" ? "\r" : " ";
                var point = button.TranslatePoint(new Point(2, 2), response)!.Value;
                if (state is "pointer") { response.MouseDown(point, MouseButton.Left); }
                else { response.KeyPress(key, RawInputModifiers.None, physical, symbol); }
                await f.Voice.Started.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                f.Main.IsListening.Should().BeTrue();
                f.Voice.StartCalls.Should().Be(1);
                button.IsEnabled.Should().BeTrue();
                if (state is "pointer") { response.MouseUp(point, MouseButton.Left); }
                else if (state is "focus-loss") { response.FindControl<TextBox>("CommandInput")!.Focus(); }
                else if (state is "hide") { response.Hide(); }
                else if (state is "close") { response.Close(); }
                else { response.KeyRelease(key, RawInputModifiers.None, physical, symbol); }
                f.Voice.EndCalls.Should().Be(1);
                f.Voice.IsCaptureQuiescent.Should().BeTrue();
                f.Main.IsListening.Should().BeFalse();
            }
            finally { response.Close(); }
        });
    }

    [Fact]
    public async Task Typed_create_review_save_model_proposal_and_dirty_close_use_one_native_workflow()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
            var store = new WindowsCustomizationStore(f, Path.Combine(f.LocalRoot, "test-editor"), Path.Combine(f.LocalRoot, "test-user"));
            var use = new ArtifactUsePreferences(f);
            using var catalogue = EmbeddedArtifactCatalogue.Load();
            var management = new CustomizationManagementService(store, admission, use, catalogue, f.Audit);
            var directories = new LocalArtifactDirectoryPreferences(f);
            using var reload = new ArtifactCatalogueReloadService(catalogue, f, directories,
                new ArtifactDirectoryConfigurationService(directories, f.Audit),
                new McpServerConfigurationService(f, directories, Path.Combine(f.LocalRoot, "test-editor")),
                NullLogger<ArtifactCatalogueReloadService>.Instance, profileRoamingRoot: Path.Combine(f.LocalRoot, "test-editor"));
            var authoring = new Authoring();
            using var controller = new CustomizationWindowController(f.Main, management, authoring, catalogue, reload,
                () => true, () => 1, NullLogger<CustomizationWindowController>.Instance);
            CustomizationWindow? editor = null;
            var nextReview = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((view, _) =>
            {
                if (view is CustomizationWindow selected) { editor = selected; }
                else { nextReview.TrySetResult(view); }
            });
            controller.Bind();
            async Task Run(string command)
            {
                f.Main.CommandText = command;
                await f.Main.RunTypedCommand.ExecuteAsync().WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            }
            void Choose(Window view, string caption) => view.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, caption)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Run("show customizations");
            editor.Should().NotBeNull();
            editor!.Kinds.Items.Cast<string>().Should().Equal("Instructions", "Skills", "Agents", "MCP Servers", "Prompts");
            editor.Kind.Should().Be(CustomizationKind.Instruction);
            (string Command, CustomizationKind Kind)[] categories =
            [
                ("show instructions", CustomizationKind.Instruction),
                ("show skills", CustomizationKind.Skill),
                ("show agents", CustomizationKind.Agent),
                ("show mcp servers", CustomizationKind.McpServer),
                ("show prompts", CustomizationKind.Prompt),
            ];
            for (var index = 0; index < categories.Length; index++)
            {
                editor.Kinds.SelectedIndex = index;
                editor.Kind.Should().Be(categories[index].Kind);
            }
            for (var index = 0; index < categories.Length; index++)
            {
                await Run(categories[index].Command);
                editor.Kinds.SelectedIndex.Should().Be(index);
                editor.Kind.Should().Be(categories[index].Kind);
            }
            await Run("add an instruction named example");
            editor.Should().NotBeNull();
            editor!.Editor.IsReadOnly.Should().BeFalse();
            editor.Editor.Text += "Manual change.";
            var save = Run("show skills");
            await save;
            editor.Kinds.SelectedIndex.Should().Be(1);
            editor.Kind.Should().Be(CustomizationKind.Skill);
            editor.Editor.Text.Should().Contain("Manual change.", "changing the browsing category does not discard the draft");
            editor.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Review and save"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.WhenAny(nextReview.Task, controller.WaitForOperation());
            nextReview.Task.IsCompleted.Should().BeTrue(editor.Status.Text);
            var review = await nextReview.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            editor.Editor.Text += "Edited during review.";
            Choose(review, "Save disabled");
            await controller.WaitForOperation().WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            File.Exists(Path.Combine(f.RoamingRoot, "Instructions", "example.instructions.md")).Should().BeFalse();
            nextReview = new(TaskCreationOptions.RunContinuationsAsynchronously);
            editor.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Review and save"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var exact = await nextReview.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Choose(exact, "Save disabled");
            await controller.WaitForOperation().WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            File.ReadAllText(Path.Combine(f.RoamingRoot, "Instructions", "example.instructions.md"))
                .Should().Contain("Edited during review.");
            management.IsDisabled((await store.List(TestContext.Current.CancellationToken)).Single()).Should().BeTrue();
            authoring.Calls.Should().Be(0);
            nextReview = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var refine = Run("refine this artifact explain this guidance");
            var disclosure = await nextReview.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            authoring.Calls.Should().Be(0, "opening an authoring request does not authorize model disclosure");
            nextReview = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Choose(disclosure, "Send for local authoring");
            var proposal = await nextReview.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            authoring.Calls.Should().Be(1);
            Choose(proposal, "Cancel");
            await refine;
            editor.Editor.Text.Should().NotContain("Model proposal.");
            nextReview = new(TaskCreationOptions.RunContinuationsAsynchronously);
            refine = Run("refine this artifact Apply a refinement");
            disclosure = await nextReview.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            nextReview = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Choose(disclosure, "Send for local authoring");
            proposal = await nextReview.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Choose(proposal, "Use proposed draft");
            await refine;
            editor.Editor.Text.Should().Contain("Model proposal.");
            File.ReadAllText(Path.Combine(f.RoamingRoot, "Instructions", "example.instructions.md")).Should().NotContain("Model proposal.");
            nextReview = new(TaskCreationOptions.RunContinuationsAsynchronously);
            editor.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Review and save"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            exact = await nextReview.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Choose(exact, "Save disabled");
            await controller.WaitForOperation().WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            File.ReadAllText(Path.Combine(f.RoamingRoot, "Instructions", "example.instructions.md")).Should().Contain("Model proposal.");
            editor.Editor.Text += "Unsaved.";
            nextReview = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var closing = f.Main.TryPrepareHandoffAsync();
            var dirty = await nextReview.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Choose(dirty, "Cancel");
            (await closing).Should().BeFalse();
            editor.Editor.Text.Should().EndWith("Unsaved.");
            var savedText = File.ReadAllText(Path.Combine(f.RoamingRoot, "Instructions", "example.instructions.md"));
            authoring.Pause = new(TaskCreationOptions.RunContinuationsAsynchronously);
            authoring.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            nextReview = new(TaskCreationOptions.RunContinuationsAsynchronously);
            refine = Run("refine this artifact A late private proposal");
            disclosure = await nextReview.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Choose(disclosure, "Send for local authoring");
            await authoring.Started.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            f.Main.CloseForObservedPrivacyEvent("Synthetic privacy closure", hidePresentation: true);
            await f.Main.PrivacyClosureTask;
            editor.IsVisible.Should().BeFalse();
            editor.Editor.Text.Should().BeEmpty();
            editor.Request.Text.Should().BeEmpty();
            editor.Files.ItemsSource.Should().BeNull();
            authoring.Pause.SetResult();
            await refine;
            editor.Editor.Text.Should().BeEmpty("late generation cannot repopulate a retired private window");
            File.ReadAllText(Path.Combine(f.RoamingRoot, "Instructions", "example.instructions.md")).Should().Be(savedText);
        });
    }

    private sealed class Authoring : ICustomizationAuthoring
    {
        internal CustomizationVoiceIntent Intent { get; set; } = CustomizationVoiceIntent.Edit;
        internal int InterpretCalls { get; private set; }
        internal bool PreserveFiles { get; set; }
        public Task<CustomizationVoiceIntent> Interpret(CustomizationVoiceContext context, string requestText,
            Func<bool> eligible, CancellationToken token)
        {
            InterpretCalls++;
            return Task.FromResult(Intent);
        }
        internal int Calls { get; private set; }
        internal TaskCompletionSource? Pause { get; set; }
        internal TaskCompletionSource? Started { get; set; }
        internal CustomizationAuthoringContext? Context { get; private set; }
        public async Task<CustomizationAuthoringResult> Refine(CustomizationSource source, IReadOnlyList<CustomizationFile> files,
            CustomizationAuthoringContext context, string requestText, Func<bool> eligible, CancellationToken token)
        {
            Calls++;
            Context = context;
            Started?.TrySetResult();
#pragma warning disable VSTHRD003 // Controlled provider barrier deliberately ignores cancellation until released.
            if (Pause is { } pause) { await pause.Task; }
#pragma warning restore VSTHRD003
            return new CustomizationAuthoringResult("Proposed edit",
                files.Select(file => !PreserveFiles && string.Equals(file.Name, context.Component ?? source.PrimaryFile, StringComparison.Ordinal)
                    ? file.WithText(file.Text + "\nModel proposal.") : file).ToArray());
        }
    }
}