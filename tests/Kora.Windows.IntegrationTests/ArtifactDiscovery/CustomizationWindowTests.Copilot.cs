using System.Security.Principal;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Core.Agents;
using Kora.Core.Customizations;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Definitions.Artifacts;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class CustomizationWindowTests
{
    [Theory]
    [InlineData("read")]
    [InlineData("hosted-source")]
    [InlineData("retention")]
    [InlineData("changed-task")]
    [InlineData("complete")]
    [InlineData("dirty-draft")]
    [InlineData("missing-source")]
    [InlineData("workflow-unavailable")]
    [InlineData("stop-join")]
    [InlineData("task-join")]
    [InlineData("close-join")]
    public Task SelectedCopilotNativeUiRequiresAllSeparateDecisionsBeforeCanonicalRetention(string stop) =>
        SelectedHostedNativeUiRequiresDecisions(stop, "Copilot");

    private Task SelectedHostedNativeUiRequiresDecisions(string stop, string provider) =>
        HeadlessSession.RunAsync(async () =>
        {
            await using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var profile = Path.Combine(f.LocalRoot, "copilot-profile");
            var store = new WindowsCustomizationStore(f, profile, Path.Combine(f.LocalRoot, "copilot-user"));
            var source = await store.Create(CustomizationKind.Agent, CustomizationScope.Kora, "reviewed", f.Token);
            var original = OwnedAgentProfileDocument.ProposeToolFree("reviewed", string.Empty, "Reviewed", "Use brief paragraphs.").Document;
            using var identity = WindowsIdentity.GetCurrent();
            var owner = identity.User ?? throw new InvalidOperationException("The private fixture owner is unavailable.");
            if (!Directory.Exists(f.RoamingRoot)) { RestrictedStorageDirectory.CreateRestrictedDirectory(f.RoamingRoot, owner); }
            RestrictedStorageDirectory.CreateRestrictedDirectory(Path.GetDirectoryName(source.Location)!, owner);
            WindowsOwnedInstructionStoreTests.WriteOwnedFile(source.Location, original.CopyBytes());
            var use = new ArtifactUsePreferences(f);
            using var catalogue = EmbeddedArtifactCatalogue.Load();
            var paused = stop.EndsWith("-join", StringComparison.Ordinal) ? new PausedCustomizationStore(store) : null;
            ICustomizationStore sources = paused is null ? store : paused;
            var management = new CustomizationManagementService(sources, admission, use, catalogue, f.Audit);
            var directories = new LocalArtifactDirectoryPreferences(f);
            using var reload = new ArtifactCatalogueReloadService(catalogue, f, directories,
                new ArtifactDirectoryConfigurationService(directories, f.Audit),
                new McpServerConfigurationService(f, directories, profile),
                NullLogger<ArtifactCatalogueReloadService>.Instance, profileRoamingRoot: profile);
            using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var observation = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
            var registry = new ReadOnlyCapabilityRegistry(f.Access, new(), new(), new(new Kora.Application.AssemblyApplicationInfo()),
                new(dependencies), new(observation), new(observation), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            await using var models = new ModelTurnHost(f.Interactions, f.Access, f.Access, registry, f.Audit,
                NullLogger<ModelTurnHost>.Instance, f.Time, []);
            await using var conversation = new SessionConversationService(f.Interactions, f.Interactions, f.Sessions,
                f.Access, models, f.Time, NullLogger<SessionConversationService>.Instance, execution: models, cancellationAudit: f.Audit);
            var target = await f.Sessions.CreateAsync(new("Selected Copilot target"), RequestOrigin.LocalUi, f.Token);
            var authoring = new Authoring();
            using var controller = new CustomizationWindowController(f.Main, management, authoring, catalogue, reload,
                () => true, () => 1, NullLogger<CustomizationWindowController>.Instance,
                copilotWorkflow: stop is "workflow-unavailable" ? null
                    : () => new(management, f.Sessions, conversation, models, f.Audit, f.Time),
                claudeWorkflow: stop is "workflow-unavailable" ? null
                    : () => new(management, f.Sessions, conversation, models, f.Audit, f.Time, Kora.Core.Providers.ProviderService.Claude));
            CustomizationWindow? editor = null;
            var selectionStatuses = new List<string?>();
            var next = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((view, _) =>
            {
                if (view is CustomizationWindow selected)
                {
                    editor = selected;
                    selected.Status.PropertyChanged += (_, change) =>
                    {
                        if (change.Property == TextBlock.TextProperty && selectionStatuses.Count < 16)
                        { selectionStatuses.Add(selected.Status.Text); }
                    };
                }
                else { next.TrySetResult(view); }
            });
            controller.Bind();
            if (stop is "missing-source") { controller.Open(); }
            else
            {
                f.Main.CommandText = "edit reviewed agent";
                await f.Main.RunTypedCommand.ExecuteAsync();
            }
            await controller.WaitForOperation();
            editor.Should().NotBeNull();
            if (stop is not "missing-source")
            {
                editor!.Details.Text.Should().Contain(source.Location,
                    "the typed command must select the exact fixture source before Copilot dispatch; selection status: {0}; initial statuses: {1}; visible inventory: {2}",
                    editor.Status.Text, string.Join(" -> ", selectionStatuses), editor.View.FilterSummary.Text);
                editor.Editor.Text.Should().Be(original.Text,
                    "the selected source must load completely before Copilot dispatch; selection status: {0}",
                    editor.Status.Text);
            }
            async Task<Window> NextReview()
            {
                await Task.WhenAny(next.Task, controller.WaitForOperation()).WaitAsync(f.Token);
                next.Task.IsCompleted.Should().BeTrue(editor!.Status.Text);
                return await next.Task;
            }
            editor!.View.CopilotTarget.Text = target.Metadata!.Name.Value;
            const string body = "  original UI task\r\n unchanged  ";
            editor.Request.Text = body;
            if (stop is "dirty-draft") { editor.Editor.Text += "\nUnsent draft change."; }
            if (paused is not null) { paused.Pause = true; }
            Choose(editor, "Run selected with " + provider);
            if (stop is "dirty-draft" or "missing-source" or "workflow-unavailable")
            {
                await controller.WaitForOperation();
                editor.Status.Text.Should().Be((stop switch
                {
                    "dirty-draft" => "Save or reload the current customization draft before running the selected source with Copilot. No request was submitted.",
                    "missing-source" => "Select and load one saved exact source before running it with Copilot. No request was submitted.",
                    _ => "Selected Copilot workflow is unavailable. No source was read or request submitted.",
                }).Replace("Copilot", provider, StringComparison.Ordinal));
                next.Task.IsCompleted.Should().BeFalse();
                (await f.Interactions.ReadConversationPageAsync(target.Authority.SessionId, null, 25, f.Token)).Messages.Should().BeEmpty();
                (await File.ReadAllBytesAsync(source.Location, f.Token)).Should().Equal(original.CopyBytes());
                authoring.Calls.Should().Be(0);
                controller.Dispose();
                return;
            }
            var read = await NextReview();
            ReadText(read).Should().Contain(source.Location).And.Contain(source.PrimaryFile);
            if (stop is "read") { Choose(read, "Cancel"); }
            else
            {
                next = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Choose(read, "Read this exact source");
                if (paused is not null)
                {
                    await paused.Entered.Task.WaitAsync(f.Token);
                    var owned = controller.WaitForOperation();
                    try
                    {
                        var other = provider is "Claude" ? "Copilot" : "Claude";
                        Choose(editor, "Run selected with " + other);
                        paused.PausedReads.Should().Be(1);
                        if (stop is "task-join") { editor.Request.Text = "Changed original task"; }
                        else if (stop is "close-join") { editor.Close(); }
                        else { Choose(editor, "Stop selected " + other + " request"); }
                        owned.IsCompleted.Should().BeFalse("cancellation or closing cannot release the selected slot before the read joins");
                        (await f.Interactions.ReadConversationPageAsync(target.Authority.SessionId, null, 25, f.Token)).Messages.Should().BeEmpty();
                    }
                    finally { paused.Release.TrySetResult(); }
                    await owned.WaitAsync(f.Token);
                    paused.PausedReads.Should().Be(1);
                    (await f.Interactions.ReadConversationPageAsync(target.Authority.SessionId, null, 25, f.Token)).Messages.Should().BeEmpty();
                    editor.Close();
                    return;
                }
                var sourceReview = await NextReview();
                ReadText(sourceReview).Should().Contain(original.Text).And.Contain(body).And.Contain(target.Metadata.Name.Value);
                (await f.Interactions.ReadConversationPageAsync(target.Authority.SessionId, null, 25, f.Token)).Messages.Should().BeEmpty();
                if (stop is "hosted-source") { Choose(sourceReview, "Cancel"); }
                else
                {
                    next = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    Choose(sourceReview, "Authorize this exact hosted copy");
                    var retention = await NextReview();
                    ReadText(retention).Should().Contain(body).And.Contain(original.Text);
                    (await f.Interactions.ReadConversationPageAsync(target.Authority.SessionId, null, 25, f.Token)).Messages.Should().BeEmpty();
                    if (stop is "retention") { Choose(retention, "Cancel"); }
                    else
                    {
                        if (stop is "changed-task") { editor.Request.Text = "Replacement task must not be sent"; }
                        next = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        Choose(retention, "Retain and submit this one request");
                        if (stop is "complete")
                        {
                            var result = await NextReview();
                            ReadText(result).Should().Contain("No assistant answer retained.");
                            Choose(result, "Close");
                        }
                    }
                }
            }
            await controller.WaitForOperation();
            var messages = (await f.Interactions.ReadConversationPageAsync(target.Authority.SessionId, null, 25, f.Token)).Messages;
            if (stop is "complete")
            {
                var message = messages.Should().ContainSingle().Subject;
                message.Body.Should().Be(body);
                message.AssistantResponse.Should().BeNull();
                message.Outcome.Should().NotBe(Kora.Core.Dependencies.ModelTurnOutcome.Succeeded);
            }
            else { messages.Should().BeEmpty(); }
            (await File.ReadAllBytesAsync(source.Location, f.Token)).Should().Equal(original.CopyBytes());
            authoring.Calls.Should().Be(0);
            editor.Close();
        });

    private static void Choose(Window view, string caption) => view.GetVisualDescendants().OfType<Button>()
        .Single(button => Equals(button.Content, caption)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static string ReadText(Window view) => string.Join("\n", view.GetVisualDescendants().OfType<TextBox>().Select(box => box.Text));
}
