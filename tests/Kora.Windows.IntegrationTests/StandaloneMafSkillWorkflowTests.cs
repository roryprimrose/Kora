using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Skills;
using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Core.Tools;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Dependencies;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Skills;
using Kora.Windows.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class StandaloneMafSkillWorkflowTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task NativeCompleteReviewRunsRealMafOnlyAfterConfirmationAndRetainsOneFinalAnswer(bool shared, bool references) =>
        RunStandaloneMafSkillWorkflow(shared, references);

    [Fact]
    public async Task OwnedCompleteReviewThroughRealSqliteRetainsTheCanonicalMafAnswer()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            f.Time.UseFrozenTimers = true;
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var transport = new OllamaModelTurnAdapterTests.ToolFreeSkillTransport();
            await using var adapter = new BoundedModelTurnAdapter(transport, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance);
            await using var composed = new Composition(f, adapter);
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            var store = new WindowsOwnedDeclarativeSkillStore(native);
            var document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "Literal guidance");
            await store.Publish(document, static () => true, commit => Task.FromResult(commit()), native.Token);
            await using var admission = new OwnedSkillControlAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            var workflow = new OwnedSkillInvocationWorkflow(store, admission, composed.Access, f.Store, composed.Workspace,
                composed.Conversation, native.Audit, f.Time, new ArtifactUsePreferences(f.Paths));
            var authority = new OwnedSkillAuthoringAuthority(RequestOrigin.LocalUi, static () => true);
            using var review = await workflow.ReviewAsync("example", "1.0.0", authority, static () => true, native.Token);
            await workflow.EnableAsync(review, authority, static () => true, native.Token);
            var targets = await workflow.ReadTargetsAsync(null, authority, static () => true, native.Token);
            using var invocation = await workflow.PrepareAsync(review, targets.Records.Single(), "Original unchanged task",
                authority, static () => true, native.Token);
            transport.Requests.Should().BeEmpty();
            var result = await workflow.InvokeAsync(invocation, authority, static () => true, native.Token);
            result.Message.AssistantResponse!.Body.Should().Be(OllamaModelTurnAdapterTests.ToolFreeSkillTransport.Answer);
            transport.Requests.Should().ContainSingle();
            transport.AgentFrameworkBridgeCalls.Should().Be(1);
        });
    }

    private static async Task RunStandaloneMafSkillWorkflow(bool shared, bool references)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            f.Time.UseFrozenTimers = true;
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var transport = new OllamaModelTurnAdapterTests.ToolFreeSkillTransport();
            await using var adapter = new BoundedModelTurnAdapter(transport, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance);
            await using var composed = new Composition(f, adapter: adapter);
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            const string body = "  Original native task e\u0301\r\n unchanged.  ";
            async Task Command(string text) { native.Main.CommandText = text; await native.Main.RunTypedCommand.ExecuteAsync(); }
            async Task Confirm(Func<Task> confirm, Func<string?> status)
            {
                transport.Requests.Should().BeEmpty();
                f.Count("conversation_messages").Should().Be(0);
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                transport.DuringModel = async () => { entered.TrySetResult(); await release.Task; };
                var pending = confirm();
                try
                {
                    await Task.WhenAny(entered.Task, pending).WaitAsync(TimeSpan.FromSeconds(15), native.Token);
                    if (!entered.Task.IsCompleted)
                    {
                        await pending;
                        throw new InvalidOperationException($"Confirmation completed without a model call: {status()}");
                    }
                    pending.IsCompleted.Should().BeFalse();
                    composed.Turns.IsQuiescent.Should().BeFalse();
                    var running = await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token);
                    running.Messages.Should().ContainSingle().Which.AssistantResponse.Should().BeNull();
                    transport.StreamingCalls.Should().Be(0);
                }
                finally { release.TrySetResult(); }
                await pending;
                var retained = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
                retained.Body.Should().Be(body);
                retained.Request.SessionId.Should().Be(f.Request.SessionId);
                retained.AssistantResponse!.Body.Should().Be(OllamaModelTurnAdapterTests.ToolFreeSkillTransport.Answer);
                var request = transport.Requests.Should().ContainSingle().Subject;
                request.Context.UserRequest.Should().Be(body);
                request.Context.Tools.Should().BeEmpty();
                request.Context.Constraints!.MaximumCalls.Should().Be(0);
                request.Context.Constraints.MaximumRounds.Should().Be(1);
                request.ClarificationAllowed.Should().BeFalse();
                request.Context.Evidence.Should().OnlyContain(item => item.Disclosure == ModelEvidenceDisclosure.LocalOnly);
                transport.AgentFrameworkBridgeCalls.Should().Be(1);
                transport.StreamingCalls.Should().Be(0);
                composed.Turns.IsQuiescent.Should().BeTrue();
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
            }
            if (shared)
            {
                var reader = new InvocationReader(references);
                var preferences = new LocalSharedSkillPreferences(native);
                var discovery = new SharedSkillDiscoveryService(preferences, reader, native.SharedAdmission, composed.Audit,
                    NullLogger<SharedSkillDiscoveryService>.Instance);
                await discovery.LoadSourcesAsync(static () => true, native.Token);
                await discovery.RegisterAsync("native selection", static () => true, native.Token);
                await using var use = new SharedSkillUseAdmission(native.Interactions, native.Interactions, new(native.Tasks));
                var workflow = new SharedSkillInvocationWorkflow(discovery, use, composed.Access, f.Store, composed.Workspace,
                    composed.Conversation, composed.Audit, f.Time);
                using var packages = new SkillPackagesWindowController(native.Main, static () => true,
                    NullLogger<SkillPackagesWindowController>.Instance, new(NullLogger<NativeDetailRenderer>.Instance),
                    discovery, NullLogger<SharedSkillSourcesWindowController>.Instance);
                using var controller = new SharedSkillInvocationController(native.Main, workflow, packages,
                    static () => true, static () => 1, NullLogger<SharedSkillInvocationController>.Instance);
                controller.Bind();
                SharedSkillSourcesWindow? inspector = null;
                SharedSkillInvocationWindow? invoking = null;
                var useOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var inspected = Window.WindowOpenedEvent.AddClassHandler<SharedSkillSourcesWindow>((window, _) => inspector = window);
                using var observed = Window.WindowOpenedEvent.AddClassHandler<SharedSkillInvocationWindow>((window, _) =>
                { invoking = window; useOpened.TrySetResult(); });
                packages.TryOpen(SkillReviewSurface.SharedSources, static () => true).Should().BeTrue();
                var sources = inspector ?? throw new InvalidOperationException("No admitted shared-source inspector.");
                var refresh = sources.FindControl<Button>("SearchSharedPackages")!;
                if (!refresh.IsEnabled)
                {
                    var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    refresh.PropertyChanged += (_, args) =>
                    {
                        if (args.Property == Avalonia.Input.InputElement.IsEnabledProperty && refresh.IsEnabled)
                        { refreshed.TrySetResult(); }
                    };
                    await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(15), native.Token);
                }
                sources.FindControl<ComboBox>("SourceSelector")!.SelectedIndex = 0;
                sources.SetCatalogue(await discovery.DiscoverAsync(reader.Source, static () => true, native.Token));
                sources.FindControl<Button>("ReviewInstructionUse")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await useOpened.Task.WaitAsync(TimeSpan.FromSeconds(15), native.Token);
                var opened = invoking ?? throw new InvalidOperationException("No shared complete review.");
                var buttons = ((StackPanel)((ScrollViewer)opened.View.Content!).Content!).Children.OfType<Button>().ToArray();
                async Task Click(string label)
                {
                    buttons.Single(button => Equals(button.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await controller.ActiveOperation;
                }
                if (references)
                {
                    reader.ReferenceReads.Should().Be(0);
                    opened.ReadReferences.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await controller.ActiveOperation;
                    reader.ReferenceReads.Should().Be(2);
                }
                var completeReview = opened.Instructions.Text!;
                completeReview.Should().EndWith(JsonSerializer.Serialize(ToolFreeSkillConstraints.Instance));
                await Click("Enable exact reviewed revision");
                await Click("Refresh existing Active conversations");
                opened.Targets.SelectedIndex = 0;
                opened.Request.Text = body;
                await Click("Review exact invocation");
                opened.Confirmation.Text.Should().Be(
                    $"{completeReview}\n\nTARGET {f.Request.SessionId.Value:D}\nORIGINAL REQUEST\n{body}");
                await Confirm(async () =>
                {
                    opened.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await controller.ActiveOperation;
                }, () => opened.Status.Text);
                opened.Status.Text.Should().Contain(OllamaModelTurnAdapterTests.ToolFreeSkillTransport.Answer);
                var context = transport.Requests.Single().Context;
                context.Evidence[0].Content.Should().Be(reader.Text);
                context.Evidence.Should().HaveCount(references ? 2 : 1);
                if (references) { context.Evidence[1].Content.Should().Contain(reader.Reference); }
                completeReview.Should().EndWith(JsonSerializer.Serialize(context.Constraints));
                opened.Close();
                opened.Instructions.Text.Should().BeEmpty();
            }
            else
            {
                var store = new WindowsOwnedDeclarativeSkillStore(native);
                var document = references
                    ? OwnedDeclarativeSkillDocument.CreatePackage("example", "1.0.0", "Example", "Description",
                        "Review [notes](notes.md).", new Dictionary<string, byte[]>(StringComparer.Ordinal)
                        { ["notes.md"] = "Exact inert reference\r\n"u8.ToArray() })
                    : OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "Literal guidance");
                await store.Publish(document, static () => true, commit => Task.FromResult(commit()), native.Token);
                await using var admission = new OwnedSkillControlAdmission(native.Interactions, native.Interactions, new(native.Tasks));
                var workflow = new OwnedSkillInvocationWorkflow(store, admission, composed.Access, f.Store, composed.Workspace,
                    composed.Conversation, native.Audit, f.Time, new ArtifactUsePreferences(f.Paths));
                await using var controller = new OwnedSkillInvocationController(native.Main, workflow, static () => true,
                    static () => 1, NullLogger<OwnedSkillInvocationController>.Instance);
                controller.Bind();
                await using var editor = new OwnedSkillEditorController(native.Main, new(store, admission, native.Audit),
                    admission, static () => true, static () => 1, NullLogger<OwnedSkillEditorController>.Instance, controller);
                editor.Bind();
                OwnedSkillInvocationWindow? invoking = null;
                using var observed = Window.WindowOpenedEvent.AddClassHandler<OwnedSkillInvocationWindow>((window, _) => invoking = window);
                await Command("owned skill use example 1.0.0");
                var opened = invoking ?? throw new InvalidOperationException("No owned complete review.");
                if (references) { await Command("owned skill read-references"); }
                await Command("owned skill enable");
                await Command("owned skill targets");
                opened.Targets.SelectedIndex = 0;
                opened.Request.Text = body;
                await Command("owned skill review-use");
                var confirmation = opened.Confirmation.Text ?? throw new InvalidOperationException("No complete owned review.");
                using var preview = JsonDocument.Parse(confirmation[confirmation.IndexOf('{')..]);
                preview.RootElement.GetProperty("UserRequest").GetString().Should().Be(body);
                preview.RootElement.GetProperty("Constraints").GetProperty("MaximumCalls").GetInt32().Should().Be(0);
                await Confirm(() => Command("owned skill invoke"), () => opened.Status.Text);
                opened.Status.Text.Should().Contain(OllamaModelTurnAdapterTests.ToolFreeSkillTransport.Answer);
                var context = transport.Requests.Single().Context;
                context.Evidence[0].Content.Should().Be(document.ManifestText);
                context.Evidence[1].Content.Should().Be(document.Guidance);
                preview.RootElement.GetProperty("Constraints").GetRawText().Should().Be(JsonSerializer.Serialize(context.Constraints));
                context.Evidence.Should().HaveCount(references ? 3 : 2);
                await Command("owned skill close-use");
                opened.Instructions.Text.Should().BeEmpty();
            }
        });
    }

    private sealed class Composition : IAsyncDisposable
    {
        private readonly AudioControlAdmission admission;
        internal WindowsSqliteSessionWorkspaceTests.Access Access { get; } = new();
        internal AuditLog Audit { get; } = new();
        internal SessionWorkspaceService Workspace { get; }
        internal SessionConversationService Conversation { get; }
        internal ModelTurnHost Turns { get; }

        internal Composition(InteractionStorageFixture fixture, IModelTurnAdapter adapter)
        {
            Workspace = WindowsSqliteSessionWorkspaceTests.Service(fixture, Access);
            admission = new(fixture.Store, fixture.Store, new(fixture.Tasks));
            var mode = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(fixture.Paths), admission, Audit);
            mode.Observe();
            var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var boundary = new Boundary();
            var runtimes = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
            var registry = new ReadOnlyCapabilityRegistry(boundary, new(), new(), new(boundary), new(dependencies),
                new(runtimes), new(runtimes), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            Turns = new(fixture.Store, Access, boundary, registry, Audit, NullLogger<ModelTurnHost>.Instance, fixture.Time,
                [new(ModelProviderSelection.OllamaCandidate,
                    ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                    fixture.Time.Now.AddMinutes(5), adapter)], mode);
            Conversation = new(fixture.Store, fixture.Store, Workspace, Access, Turns, fixture.Time,
                NullLogger<SessionConversationService>.Instance, execution: Turns, cancellationAudit: Audit,
                interactions: fixture.Store, explicitTaskCoordinator: new(fixture.Tasks));
        }

        public async ValueTask DisposeAsync()
        {
            await Conversation.DisposeAsync();
            await Turns.DisposeAsync();
            await admission.DisposeAsync();
        }
    }

    private sealed class Boundary : ICapabilityHostAccess, IApplicationInfo
    {
        public bool IsCurrentHost => true;
        public string Version => "1.0.0";
    }

    private sealed class InvocationReader(bool references) : ISharedSkillSourceReader
    {
        internal SharedSkillSource Source { get; } = new(Guid.NewGuid(), ".agents\\skills", new string('a', 48));
        internal string Text => "---\nname: concise-review\nversion: 1.0.0\ndescription: Review prose\n"
            + (references ? "format: skill-md-instructions-v2\nreferences: notes.md\n" : "")
            + "---\nReview user prose concisely.";
        internal string Reference => "# Notes\nExplain the exact original prose.";
        internal int ReferenceReads { get; private set; }

        private SharedSkillSnapshot Snapshot()
        {
            var snapshot = SharedSkillSnapshot.Parse(Source.Id, "concise-review\\SKILL.md", Encoding.UTF8.GetBytes(Text),
                references, references ? ["concise-review\\notes.md"] : []);
            return references ? snapshot.WithPackageDirectoryIdentity(new string('b', 48)) : snapshot;
        }

        public ValueTask<SharedSkillSource> SelectAsync(string selectedRoot, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Source);

        public ValueTask<SharedSkillCatalogue> DiscoverAsync(SharedSkillSource source, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new SharedSkillCatalogue(Source, [Snapshot()]));

        public ValueTask<SharedSkillSnapshot> ReadReferencesAsync(
            SharedSkillSource source, SharedSkillSnapshot primary, CancellationToken cancellationToken)
        {
            ReferenceReads++;
            var snapshot = Snapshot();
            return ValueTask.FromResult(snapshot.WithReferenceBundle(SharedSkillReferenceBundle.Create(snapshot,
                new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["notes.md"] = Encoding.UTF8.GetBytes(Reference) })));
        }
    }

    private sealed class AuditLog : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent auditEvent)
        {
            HostActivity.RequireCurrent();
            Events.Add(auditEvent);
        }
    }

}
