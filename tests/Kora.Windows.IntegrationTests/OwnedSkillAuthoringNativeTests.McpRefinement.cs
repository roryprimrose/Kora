using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Mcp;
using Kora.Application.Skills;
using Kora.Application.Tools;
using Kora.Application.Voice;
using Kora.Core.Dependencies;
using Kora.Core.Mcp;
using Kora.Core.Skills;
using Kora.NativeUxFixture;
using Kora.Windows.Skills;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class OwnedSkillAuthoringNativeTests
{
    [Theory]
    [InlineData("save")]
    [InlineData("reject")]
    [InlineData("cancel-publication")]
    [InlineData("withdraw")]
    [InlineData("missing-proof")]
    public async Task NativeReopenedMcpGuidanceUsesToolsNoneAndSeparatelyPublishesDisabledVersion(string scenario)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            await using var admission = new OwnedSkillControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var store = new WindowsOwnedDeclarativeSkillStore(f);
            var save = new OwnedSkillSaveWorkflow(store, admission, f.Audit);
            using var bootstrapper = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(bootstrapper);
            var registry = new ReadOnlyCapabilityRegistry(f.Access, new(), new(), new(new Kora.Application.AssemblyApplicationInfo()),
                new(bootstrapper), new(runtime), new(runtime), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            await using var modes = new AudioControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var modelPolicy = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(f), modes, f.Audit);
            var adapter = new SkillRefinementAdapter { Body = "NEW literal guidance; mcp.other is inert text" };
            await using var models = new ModelTurnHost(f.Interactions, f.Access, f.Access, registry, f.Audit,
                NullLogger<ModelTurnHost>.Instance, TimeProvider.System,
                [new(ModelProviderSelection.OllamaCandidate, ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                    DateTimeOffset.UtcNow.AddMinutes(5), adapter)], modelPolicy);
            await using var runner = new LocalContextTurnWorkflow(f.Interactions, f.Interactions, f.Sessions, new(f.Tasks),
                f.Access, models, models, f.Audit, NullLogger<LocalContextTurnWorkflow>.Instance);
            var account = new McpAccountReference(Guid.NewGuid(), new(1), Guid.NewGuid(), new(new("https://example.com/mcp")));
            var connection = new McpConnectionDescriptor(new string('a', 64), "Test", "unread-test-source",
                new string('b', 64), account.Destination, account, 1);
            var tool = new McpDiscoveredTool("search", new("""{"type":"object","additionalProperties":false,"properties":{},"required":[]}"""));
            using var mcpPolicy = new McpReadOnlyPolicy([new(account.Destination, account, connection.SourceRevision, tool.Name, tool.Schema.Digest)]);
            var live = true;
            if (scenario is not "missing-proof")
            { mcpPolicy.ObservePreparedDescriptor(connection, tool, () => live, TimeProvider.System, DateTimeOffset.UtcNow.AddMinutes(2)); }
            var original = OwnedDeclarativeSkillDocument.CreateMcp("example", "1.0.0", "Example", "Description", "OLD literal guidance",
                new(connection.ServerId, connection.SourceRevision, tool.Name, tool.Schema.Digest));
            var baseline = await store.Publish(original, static () => true, commit => Task.FromResult(commit()), f.Token);
            var refinement = new OwnedSkillGuidanceRefinementService(store, admission, save, runner, TimeProvider.System, mcpPolicy);
            await using var controller = new OwnedSkillEditorController(f.Main, save, admission, static () => true,
                static () => 1, NullLogger<OwnedSkillEditorController>.Instance, refinement: refinement);
            controller.Bind();
            OwnedSkillEditorWindow? editor = null;
            var next = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window is OwnedSkillEditorWindow skill) { editor = skill; }
                else { next.TrySetResult(window); }
            });
            async Task Run(string command) { f.Main.CommandText = command; await f.Main.RunTypedCommand.ExecuteAsync(); }
            async Task<Window> Review(Task operation)
            {
                (await Task.WhenAny(next.Task, operation)).Should().BeSameAs(next.Task);
                var result = await next.Task;
                next = new(TaskCreationOptions.RunContinuationsAsynchronously);
                AssertModalHandoff(editor!, result);
                return result;
            }
            static void Choose(Window dialog, string caption) => dialog.GetLogicalDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, caption)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            static string Complete(OwnedDeclarativeSkillDocument document) =>
                "manifest.yaml\n" + document.ManifestText + "\ninstructions.md\n" + document.Guidance;
            await Run("owned skill reopen example 1.0.0");
            var owned = editor!;
            owned.VersionInput.Text = "2.0.0";
            var draft = owned.Document();
            await Run("owned skill critique: Make the literal guidance clearer.");
            var refining = Run("owned skill refine");
            var disclosure = await Review(refining);
            string.Join("\n", disclosure.GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text))
                .Should().Contain("tools none").And.Contain("fixed read-only MCP");
            Choose(disclosure, "Generate local guidance proposal");
            if (scenario is not "missing-proof")
            {
                var comparison = await Review(refining);
                comparison.GetLogicalDescendants().OfType<TextBox>().Select(box => box.Text).Should().Equal(
                    draft.ManifestText + "\ninstructions.md\n" + draft.Guidance,
                    draft.ManifestText + "\ninstructions.md\n" + adapter.Body);
                Choose(comparison, scenario is "reject" ? "Reject proposal" : "Use proposal for next Save");
            }
            await refining;
            owned.Guidance.Text.Should().Be(original.Guidance);
            (await store.List(f.Token)).Should().ContainSingle();
            adapter.Calls.Should().Be(scenario is "missing-proof" ? 0 : 1);
            if (adapter.Wire is { } bytes)
            {
                using var wire = JsonDocument.Parse(bytes);
                wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
                wire.RootElement.GetProperty("ToolMode").GetInt32().Should().Be((int)ModelToolMode.None);
                wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().Be(original.Guidance);
                wire.RootElement.GetRawText().Should().NotContain(connection.ServerId).And.NotContain(connection.SourceRevision);
            }
            if (scenario is "withdraw") { live = false; }
            await Run("owned skill validate");
            var saving = Run("owned skill save");
            if (scenario is "save" or "cancel-publication")
            {
                var package = await Review(saving);
                var proposed = OwnedDeclarativeSkillDocument.Parse(draft.ManifestBytes.Span, System.Text.Encoding.UTF8.GetBytes(adapter.Body));
                package.GetLogicalDescendants().OfType<TextBox>().Select(box => box.Text).Should().Equal(Complete(original), Complete(proposed));
                Choose(package, "Continue to separate native publication confirmation");
                var publication = await Review(saving);
                (await store.List(f.Token)).Should().ContainSingle();
                Choose(publication, scenario is "save" ? "Publish exact new version" : "Cancel");
            }
            else
            {
                if (scenario is "reject" or "missing-proof")
                {
                    var manual = await Review(saving);
                    Choose(manual, "Cancel");
                }
                else { next.Task.IsCompleted.Should().BeFalse(); }
            }
            await saving;
            var target = await store.Read("example", "2.0.0", f.Token);
            if (scenario is "save")
            {
                target!.Document.ManifestBytes.ToArray().Should().Equal(draft.ManifestBytes.ToArray());
                target.Document.McpReference.Should().Be(original.McpReference);
                target.Document.Guidance.Should().Be(adapter.Body);
                target.Document.IsAvailableForInvocation.Should().BeFalse();
                owned.BasePackage!.IsSameRevision(target).Should().BeTrue();
            }
            else { target.Should().BeNull(); }
            (await store.Read("example", "1.0.0", f.Token))!.IsSameRevision(baseline).Should().BeTrue();
            models.IsQuiescent.Should().BeTrue();
            owned.Close();
        });
    }
}
