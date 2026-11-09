using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Dependencies;
using Kora.Application.Tools;
using Kora.Application.Voice;
using Kora.Core.Agents;
using Kora.Core.Artifacts;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Tools;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class AgentProfileOriginalRefinementTests
{
    [Theory]
    [InlineData("save")]
    [InlineData("native-save")]
    [InlineData("voice-save")]
    [InlineData("cancel-disclosure")]
    [InlineData("reject")]
    [InlineData("cancel-save")]
    [InlineData("uncertain-save")]
    [InlineData("original-tool")]
    [InlineData("draft-tool")]
    [InlineData("empty-draft")]
    [InlineData("original-v4")]
    [InlineData("before-source")]
    [InlineData("before-missing")]
    [InlineData("admission-source-return")]
    [InlineData("late-source")]
    [InlineData("late-replaced")]
    [InlineData("late-missing")]
    [InlineData("late-read-failure")]
    [InlineData("late-cancel")]
    [InlineData("late-draft")]
    [InlineData("late-request")]
    [InlineData("late-owner")]
    [InlineData("late-private")]
    [InlineData("late-catalogue")]
    [InlineData("late-expiry")]
    [InlineData("late-voice-consent")]
    [InlineData("adopt-source")]
    [InlineData("adopt-draft")]
    [InlineData("adopt-request")]
    [InlineData("adopt-voice-consent")]
    [InlineData("final-owner")]
    public async Task OnlyPinnedOriginalAndCurrentCritiqueCanStageThenSeparatelyUpdateDisabledSource(string scenario)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent, syntheticVoice: true);
            await f.InitializeAsync();
            var voice = scenario is "voice-save" or "late-voice-consent" or "adopt-voice-consent";
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var files = new WindowsOwnedAgentProfileStore(f);
            var store = new ObservedStore(files);
            var save = new AgentProfileSaveWorkflow(store, admission, f.Audit);
            using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
            var registry = new ReadOnlyCapabilityRegistry(f.Access, new(), new(), new(new Kora.Application.AssemblyApplicationInfo()),
                new(dependencies), new(runtime), new(runtime), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            await using var modes = new AudioControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var policy = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(f), modes, f.Audit);
            var adapter = new Adapter();
            await using var models = new ModelTurnHost(f.Interactions, f.Access, f.Access, registry, f.Audit,
                NullLogger<ModelTurnHost>.Instance, TimeProvider.System,
                [new(ModelProviderSelection.OllamaCandidate, ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                    DateTimeOffset.UtcNow.AddMinutes(5), adapter)], policy);
            await using var runner = new LocalContextTurnWorkflow(f.Interactions, f.Interactions, f.Sessions, new(f.Tasks),
                f.Access, models, models, f.Audit, NullLogger<LocalContextTurnWorkflow>.Instance);
            var clock = new Clock();
            var authoring = new CustomizationAuthoringService(admission, runner, clock);
            using var catalogue = new ArtifactCatalogue([OwnedAgentProfileDocument.ProposeToolFree("catalogue", "", "Fixture", "Inert").Document.Definition]);
            var owner = 1L;
            var controller = new AgentProfileEditorController(f.Main, save, () => true, () => owner,
                NullLogger<AgentProfileEditorController>.Instance, authoring, catalogue, clock);
            AgentProfileEditorWindow? editor = null;
            var next = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var observation = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window is AgentProfileEditorWindow selected) { editor = selected; }
                else { next.TrySetResult(window); }
            });
            controller.Bind();
            async Task Run(string command) { f.Main.CommandText = command; await f.Main.RunTypedCommand.ExecuteAsync(); }
            async Task<Window> Decision(Task operation)
            {
                (await Task.WhenAny(next.Task, operation)).Should().BeSameAs(next.Task, "expected native decision; status: {0}", editor?.Status.Text);
                var result = await next.Task.WaitAsync(TestContext.Current.CancellationToken);
                next = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return result;
            }
            var destination = await files.PrepareNew("example", f.Token);
            var source = OwnedAgentProfileDocument.ProposeToolFree("example", "", "Original role", "Complete original guidance").Document.Text
                .Replace("maximum-input-bytes: 16000", "maximum-input-bytes: 12000", StringComparison.Ordinal);
            if (scenario is "original-tool")
            { source = ToolSource(source); }
            if (scenario is "original-v4")
            {
                source = ToolSource(source).Replace("kora-agent-format: 1", "kora-agent-format: 4", StringComparison.Ordinal)
                    .Replace("references: none", "references: disk.kora-profile.instruction.example", StringComparison.Ordinal);
            }
            var originalBytes = ArtifactFileDocument.Encode(source, true);
            await File.WriteAllBytesAsync(destination.DisplayPath, originalBytes, f.Token);
            if (voice)
            {
                f.Main.BindVoiceOwnershipGate(() => true);
                await f.Main.SetVoiceConsentAsync(true);
            }
            await Run("agent author inspect example");
            editor!.Source.IsReadOnly.Should().BeTrue(
                "the original inspect operation must complete; editor status: {0}; current inspect admission: {1}; private presentation: {2}; protected call: {3}; pending interaction: {4}; controller operation: {5}",
                editor.Status.Text, f.Main.CaptureInstructionEditorEligibility().Invoke(), f.Main.CanRevealPrivatePresentation,
                f.Main.IsProtectedCall, f.Main.IsResponseInteractionPending, controller.ActiveOperation.Status);
            Click(editor, "Edit existing original");
            await controller.ActiveOperation;
            editor.NameInput.IsReadOnly.Should().BeTrue();
            if (scenario is "draft-tool") { editor.Source.Text = ToolSource(source); }
            if (scenario is "empty-draft") { editor.Source.Text = string.Empty; }
            const string critique = "  Make only this guidance shorter e\u0301\U0001f642.  ";
            if (scenario is "native-save") { editor.DescriptionRequest.Text = critique; }
            else if (voice)
            {
                var described = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                editor.DescriptionRequest.PropertyChanged += (_, args) =>
                { if (args.Property == TextBox.TextProperty) { described.TrySetResult(); } };
                await f.Main.BeginPushToTalkCommand.ExecuteAsync();
                await f.Main.EndPushToTalkCommand.ExecuteAsync();
                f.Voice.PublishTranscript(f.Main.AssistantName + ", agent author describe " + critique);
                await described.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token);
            }
            else { await Run("agent author describe " + critique); }
            var request = editor.DescriptionRequest.Text!;
            if (scenario is "before-source")
            { await File.WriteAllTextAsync(destination.DisplayPath, source + "\nExternal edit", f.Token); }
            if (scenario is "before-missing") { File.Delete(destination.DisplayPath); }
            Task generating;
            if (scenario is "native-save" || voice)
            { Click(editor, "Ask Kora: review local authoring disclosure"); generating = controller.ActiveOperation; }
            else { generating = Run("agent author ask"); }
            if (scenario is "original-tool" or "draft-tool" or "empty-draft" or "original-v4" or "before-source" or "before-missing")
            {
                await generating;
                next.Task.IsCompleted.Should().BeFalse();
                adapter.Calls.Should().Be(0);
            }
            else
            {
                var disclosure = await Decision(generating);
                Text(disclosure).Should().Contain(source).And.Contain(request).And.Contain(destination.DisplayPath)
                    .And.Contain("inspected existing original");
                store.Creates.Should().Be(0);
                store.Updates.Should().Be(0);
                (await File.ReadAllBytesAsync(destination.DisplayPath, f.Token)).Should().Equal(originalBytes);
                var late = scenario.StartsWith("late-", StringComparison.Ordinal);
                if (late) { adapter.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously); }
                if (scenario is "admission-source-return")
                {
                    store.BeforeRead = () =>
                    {
                        if (store.Reads == 5) { File.WriteAllText(destination.DisplayPath, source + "\nObserved external change"); }
                    };
                    store.AfterRead = () =>
                    {
                        if (store.Reads == 5) { File.WriteAllBytes(destination.DisplayPath, originalBytes); }
                    };
                }
                Click(disclosure, scenario is "cancel-disclosure" ? "Cancel" : "Generate local agent proposal");
                if (late)
                {
                    (await Task.WhenAny(adapter.Started.Task, generating)).Should().BeSameAs(adapter.Started.Task);
                    switch (scenario)
                    {
                        case "late-source": await File.WriteAllTextAsync(destination.DisplayPath, source + "\nExternal", f.Token); break;
                        case "late-replaced":
                            File.Delete(destination.DisplayPath);
                            await File.WriteAllBytesAsync(destination.DisplayPath, originalBytes, f.Token);
                            break;
                        case "late-missing": File.Delete(destination.DisplayPath); break;
                        case "late-read-failure": store.FailRead = true; break;
                        case "late-cancel": Click(editor, "Cancel proposal or save review"); break;
                        case "late-draft": editor.Source.Text = source + "\nIndependent edit"; break;
                        case "late-request": editor.DescriptionRequest.Text += "Changed"; break;
                        case "late-owner": owner++; break;
                        case "late-private": f.Main.CloseForObservedPrivacyEvent("Synthetic privacy withdrawal", hidePresentation: true); break;
                        case "late-catalogue": catalogue.ReportReloadFailure(new IOException("Synthetic reload failure")); break;
                        case "late-expiry": clock.Now += TimeSpan.FromMinutes(3); break;
                        case "late-voice-consent": await f.Main.SetVoiceConsentAsync(false); break;
                    }
                    generating.IsCompleted.Should().BeFalse();
                    models.IsQuiescent.Should().BeFalse();
                    adapter.Pending!.SetResult(Adapter.Reply());
                    await generating;
                    models.IsQuiescent.Should().BeTrue();
                    editor.Source.Text.Should().NotContain("Short proposed guidance");
                    if (scenario is "late-private")
                    { await f.Main.PrivacyClosureTask; editor.Source.Text.Should().BeEmpty(); return; }
                    if (scenario is "late-owner") { owner--; }
                }
                else if (scenario is "admission-source-return")
                {
                    await generating;
                    adapter.Calls.Should().Be(0);
                    editor.Source.Text.Should().Be(source);
                    (await File.ReadAllBytesAsync(destination.DisplayPath, f.Token)).Should().Equal(originalBytes);
                }
                else if (scenario is not "cancel-disclosure")
                {
                    var comparison = await Decision(generating);
                    Text(comparison).Should().Contain(source).And.Contain("Short proposed guidance").And.Contain(destination.DisplayPath)
                        .And.Contain("Save existing original");
                    editor.Source.Text.Should().Be(source);
                    switch (scenario)
                    {
                        case "adopt-source": await File.WriteAllTextAsync(destination.DisplayPath, source + "\nExternal", f.Token); break;
                        case "adopt-draft": editor.Source.Text += "\nIndependent edit"; break;
                        case "adopt-request": editor.DescriptionRequest.Text += "Changed"; break;
                        case "adopt-voice-consent": await f.Main.SetVoiceConsentAsync(false); break;
                        case "final-owner": store.AfterRead = () => owner++; break;
                    }
                    if (scenario is not ("adopt-draft" or "adopt-request" or "adopt-voice-consent"))
                    { Click(comparison, scenario is "reject" ? "Reject proposal" : "Use proposed draft"); }
                    await generating;
                    if (scenario is "final-owner") { store.AfterRead = null; owner--; }
                    if (scenario is "save" or "native-save" or "voice-save" or "cancel-save" or "uncertain-save")
                    {
                        var proposed = OwnedAgentProfileDocument.Parse("example", editor.Source.Text!);
                        foreach (var field in OwnedAgentProfileDocument.Parse("example", source).Document.Metadata.Where(field =>
                            !string.Equals(field.Key, "description", StringComparison.Ordinal)))
                        { proposed.Document.Metadata[field.Key].Should().Be(field.Value); }
                        proposed.Document.Body.Should().Be("Short proposed guidance");
                        (await File.ReadAllBytesAsync(destination.DisplayPath, f.Token)).Should().Equal(originalBytes);
                        await Run("agent author save as new");
                        editor.Confirm.IsEnabled.Should().BeFalse("an original cannot use create-only Save");
                        Click(editor, "Review Save (new or existing original)");
                        await controller.ActiveOperation;
                        editor.Review.Text.Should().Contain(source).And.Contain(editor.Source.Text).And.Contain(destination.DisplayPath);
                        if (scenario is "uncertain-save")
                        { files.AfterPublication = () => throw new IOException("Synthetic unconfirmed update"); }
                        Click(editor.Confirm);
                        var confirmation = await Decision(controller.ActiveOperation);
                        confirmation.Title.Should().Be("Confirm Save existing original");
                        store.Updates.Should().Be(0);
                        Click(confirmation, scenario is "cancel-save" ? "Cancel" : "Save existing original");
                        await controller.ActiveOperation;
                        if (scenario is "cancel-save") { store.Updates.Should().Be(0); }
                        else
                        {
                            store.Updates.Should().Be(1);
                            var actual = await files.Read("example", f.Token);
                            actual!.Document.Document.HasBom.Should().BeTrue();
                            actual.Document.Document.Text.Should().Be(editor.Source.Text);
                            if (scenario is "uncertain-save")
                            {
                                editor.Status.Text.Should().Contain("unconfirmed");
                                Click(editor, "Review Save (new or existing original)");
                                await controller.ActiveOperation;
                                editor.Confirm.IsEnabled.Should().BeFalse();
                                store.Updates.Should().Be(1);
                            }
                            else { editor.Source.IsReadOnly.Should().BeTrue(); }
                        }
                    }
                    else { editor.Source.Text.Should().NotContain("Short proposed guidance"); }
                }
                else { await generating; }
                if (adapter.Calls != 0)
                {
                    using var wire = JsonDocument.Parse(adapter.Wire!);
                    wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(request);
                    wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().Be(source);
                    wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
                    System.Text.Encoding.UTF8.GetString(adapter.Wire!).Should().NotContain(destination.DisplayPath);
                    adapter.Origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
                }
            }
            store.Creates.Should().Be(0);
            catalogue.SourceCandidates.Should().ContainSingle();
            models.IsQuiescent.Should().BeTrue();
            var closingDecision = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var closingObservation = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            { if (window.Title is "Unsaved agent draft") { closingDecision.TrySetResult(window); } });
            var closing = f.Main.ExitAsync();
            if (await Task.WhenAny(closing, closingDecision.Task) == closingDecision.Task)
            { Click(await closingDecision.Task, "Discard"); }
            await closing;
            editor.IsVisible.Should().BeFalse();
        });
    }

    private static string ToolSource(string source) => source.Replace("tools: none", "tools: application.get_version", StringComparison.Ordinal)
        .Replace("maximum-calls: 0", "maximum-calls: 1", StringComparison.Ordinal);
    private static string Text(Window window) => string.Join("\n", window.GetVisualDescendants().OfType<TextBox>().Select(box => box.Text));
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Click(Window window, string label) =>
        Click(window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, label)));

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Adapter : IModelTurnAdapter
    {
        internal int Calls;
        internal byte[]? Wire;
        internal RequestOrigin Origin;
        internal TaskCompletionSource<ModelProviderReply>? Pending;
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal static ModelProviderReply Reply() => new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
            new("{\"description\":\"Short role\",\"body\":\"Short proposed guidance\"}", null));
        public async Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, Kora.Core.Providers.ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool, CancellationToken token)
        {
            Calls++; Wire = context.ToArray(); Origin = HostActivity.RequireCurrent().Request.Origin;
            Started.TrySetResult();
            return Pending is { } pending ? await pending.Task : Reply();
        }
    }

    private sealed class ObservedStore(IOwnedAgentProfileStore inner) : IOwnedAgentProfileStore
    {
        internal int Creates;
        internal int Updates;
        internal bool FailRead;
        internal int Reads;
        internal Action? BeforeRead;
        internal Action? AfterRead;
        public async Task<OwnedAgentProfileSnapshot?> Read(string name, CancellationToken token)
        {
            Reads++;
            BeforeRead?.Invoke();
            if (FailRead) { throw new IOException("Synthetic protected read failure"); }
            var result = await inner.Read(name, token);
            AfterRead?.Invoke();
            return result;
        }
        public Task<IReadOnlyList<string>> List(CancellationToken token) => inner.List(token);
        public Task<OwnedAgentProfileDestination> PrepareNew(string name, CancellationToken token) => inner.PrepareNew(name, token);
        public Task<AgentProfileWriteResult> Create(OwnedAgentProfileDestination destination, OwnedAgentProfileDocument document,
            Func<bool> eligible, Func<Func<AgentProfileWriteResult>, Task<AgentProfileWriteResult>> commit, CancellationToken token)
        { Creates++; return inner.Create(destination, document, eligible, commit, token); }
        public Task<AgentProfileWriteResult> Update(OwnedAgentProfileSnapshot expected, OwnedAgentProfileDocument document,
            Func<bool> eligible, Func<Func<AgentProfileWriteResult>, Task<AgentProfileWriteResult>> commit, CancellationToken token)
        { Updates++; return inner.Update(expected, document, eligible, commit, token); }
    }
}
