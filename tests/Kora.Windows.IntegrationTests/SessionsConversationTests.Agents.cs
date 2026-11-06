using System.Text;
using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Application.Definitions;
using Kora.Application.Dependencies;
using Kora.Core.Agents;
using Kora.Core.Artifacts;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task NativeAgentReviewSeparateConsentRetainsRealAnswerAndClearsPrivateBuffersOnStop(bool invoke, bool referenced, bool combined)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new HistoryAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            var reader = new NativeAgentReader();
            var instructions = new AgentOwnedStore();
            var revisions = new InstructionRevisionCatalogue(f.Paths);
            var catalogue = new ArtifactCatalogue(revisions.Reconcile(instructions.Sources.Select(item =>
                item.Document.Definition with { OwnedInstructionRevision = item.Revision }).ToArray()));
            foreach (var item in instructions.Sources)
            { revisions.CompleteActivation(item, revisions.PrepareActivation(item), catalogue); }
            if (referenced)
            {
                reader.Text = reader.Text.Replace("kora-agent-format: 1", "kora-agent-format: 2", StringComparison.Ordinal)
                    .Replace("references: none", "references: disk.kora-profile.instruction.example,disk.kora-profile.instruction.second", StringComparison.Ordinal);
            }
            if (combined)
            {
                reader.Text = reader.Text.Replace("kora-agent-format: 2", "kora-agent-format: 4", StringComparison.Ordinal)
                    .Replace("tools: none", "tools: application.get_version", StringComparison.Ordinal)
                    .Replace("maximum-calls: 0", "maximum-calls: 1", StringComparison.Ordinal)
                    .Replace("maximum-rounds: 1", "maximum-rounds: 2", StringComparison.Ordinal);
            }
            await using var workflow = new BoundedAgentWorkflow(reader, f.Store, composed.Access,
                composed.Conversation, composed.Turns, composed.Audit, f.Time, new(catalogue, instructions, revisions));
            var controller = new AgentRunController(native.Main, workflow, composed.Workspace,
                () => true, () => 1, NullLogger<AgentRunController>.Instance);
            controller.Bind();
            AgentRunWindow? opened = null;
            using var observation = Window.WindowOpenedEvent.AddClassHandler<AgentRunWindow>((window, _) => opened = window);
            native.Main.CommandText = "review agent";
            await native.Main.RunTypedCommand.ExecuteAsync();
            var window = opened ?? throw new InvalidOperationException("No admitted bounded agent window.");
            try
            {
                AutomationProperties.GetName(window.Review).Should().Be("Complete exact agent guidance, tools and cumulative budget review");
                AutomationProperties.GetName(window.Confirm).Should().Be("Separate confirmation of this exact bounded agent invocation");
                var buttons = ((StackPanel)((ScrollViewer)window.Content!).Content!).Children.OfType<Button>().ToArray();
                buttons.Single(button => Equals(button.Content, "Refresh existing Active conversations"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.ActiveOperation;
                window.Targets.SelectedIndex = 0;
                window.Source.Text = @"C:\Users\owner\existing.agent.md";
                window.Input.Text = "Original native request.";
                buttons.Single(button => Equals(button.Content, "Read source and review exact invocation"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.ActiveOperation;
                if (referenced)
                {
                    window.Confirm.IsEnabled.Should().BeFalse();
                    window.Resolve.IsEnabled.Should().BeTrue();
                    window.Review.Text.Should().Contain("DECLARED OWNED INSTRUCTION IDS").And.Contain("EXACT ENABLED CANDIDATES");
                    if (combined)
                    {
                        window.Review.Text.Should().Contain("TOOLS application.get_version")
                            .And.Contain("CUMULATIVE HOST CLAMPS: calls 1, rounds 2").And.NotContain("TOOL-FREE");
                    }
                    instructions.Reads.Should().Be(0);
                    adapter.Calls.Should().Be(0);
                    if (invoke)
                    {
                        native.Main.CommandText = "agent consent resolution";
                        await native.Main.RunTypedCommand.ExecuteAsync();
                    }
                    else
                    {
                        window.Resolve.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await controller.ActiveOperation;
                    }
                    window.Review.Text.Should().Contain(instructions.Sources[0].Document.Text)
                        .And.Contain(instructions.Sources[1].Document.Text).And.Contain(reader.Text);
                    instructions.Reads.Should().Be(4);
                    adapter.Calls.Should().Be(0);
                    f.Count("conversation_messages").Should().Be(0);
                }
                window.Confirm.IsEnabled.Should().BeTrue();
                window.Review.Text.Should().Contain("Original native request.").And.Contain("LOCAL TEXT -> TEXT");
                adapter.Calls.Should().Be(0);
                if (invoke)
                {
                    window.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await controller.ActiveOperation;
                    adapter.Calls.Should().Be(1);
                    window.Status.Text.Should().Contain("Succeeded");
                    window.Result.Text.Should().Be("earlier ASSISTANT");
                    var message = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
                    message.Body.Should().Be("Original native request.");
                    message.AssistantResponse!.Body.Should().Be("earlier ASSISTANT");
                    using var wire = JsonDocument.Parse(adapter.Wire!);
                    wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(combined ? 1 : 0);
                    wire.RootElement.GetProperty("Constraints").GetProperty("MaximumCalls").GetInt32().Should().Be(combined ? 1 : 0);
                    if (referenced)
                    {
                        Encoding.UTF8.GetString(adapter.Wire!).Should().Contain("Exact owned example guidance.")
                            .And.Contain("Exact owned second guidance.");
                    }
                }
                native.Main.CommandText = "agent stop";
                await native.Main.RunTypedCommand.ExecuteAsync();
                adapter.Calls.Should().Be(invoke ? 1 : 0);
                reader.Released.Should().BeTrue();
                window.Source.Text.Should().BeEmpty();
                window.Input.Text.Should().BeEmpty();
                window.Review.Text.Should().BeEmpty();
                window.Result.Text.Should().BeEmpty();
            }
            finally { await native.Main.ExitAsync(); }
        });
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("hold")]
    [InlineData("epoch")]
    [InlineData("reload")]
    [InlineData("profile")]
    [InlineData("privacy")]
    [InlineData("input")]
    [InlineData("expired")]
    public async Task ReferencedAgentInvalidationPreventsAdmissionAndRetainedAnswer(string fault)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var adapter = new HistoryAdapter();
        await using var composed = new Composition(f, adapter: adapter);
        var sources = new AgentOwnedStore();
        var revisions = new InstructionRevisionCatalogue(f.Paths);
        var catalogue = EnabledAgentInstructions(sources, revisions);
        var reader = ReferencedAgentReader();
        await using var workflow = new BoundedAgentWorkflow(reader, f.Store, composed.Access, composed.Conversation,
            composed.Turns, composed.Audit, f.Time, new(catalogue, sources, revisions));
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var current = true;
        using var review = await workflow.ReviewAsync("selected.agent.md",
            (await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token))!, "unchanged original request",
            RequestOrigin.LocalUi, () => current, f.Token);
        await workflow.Invoking(value => value.InvokeAsync(review, RequestOrigin.LocalUi, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        sources.Reads.Should().Be(0);
        await workflow.Invoking(value => value.ResolveAsync(review, RequestOrigin.HostSystem, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        sources.Reads.Should().Be(0);
        await workflow.ResolveAsync(review, RequestOrigin.LocalUi, f.Token);
        review.IsInvocationReady.Should().BeTrue();
        await workflow.Invoking(value => value.InvokeAsync(review, f.Token)).Should().ThrowAsync<InvalidOperationException>();
        if (fault is "disable") { revisions.DisableExact(sources.Sources[0], catalogue); }
        if (fault is "hold") { revisions.HoldExact(sources.Sources[0], catalogue); }
        if (fault is "epoch") { revisions.SuspendReviewedUse(); }
        if (fault is "reload") { catalogue.ReportReloadFailure(new IOException("reload")); }
        if (fault is "profile") { reader.Text += "\nChanged"; }
        if (fault is "privacy") { composed.Access.CanControl = false; }
        if (fault is "input") { current = false; }
        if (fault is "expired") { f.Time.Now = review.ExpiresAt; }
        await workflow.Invoking(value => value.InvokeAsync(review, RequestOrigin.LocalUi, f.Token)).Should().ThrowAsync<Exception>();
        adapter.Calls.Should().Be(0);
        f.Count("conversation_messages").Should().Be(0);
        await workflow.RevokeAsync(review);
        reader.Released.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReferencedAgentResolutionRevokeJoinsIgnoringReadBeforeSourceReleaseAndNeverDiscloses(bool combined)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var adapter = new HistoryAdapter();
        await using var composed = new Composition(f, adapter: adapter);
        var sources = new AgentOwnedStore();
        var revisions = new InstructionRevisionCatalogue(f.Paths);
        var catalogue = EnabledAgentInstructions(sources, revisions);
        var reader = ReferencedAgentReader(combined);
        await using var workflow = new BoundedAgentWorkflow(reader, f.Store, composed.Access, composed.Conversation,
            composed.Turns, composed.Audit, f.Time, new(catalogue, sources, revisions));
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        using var review = await workflow.ReviewAsync("selected.agent.md",
            (await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token))!, "original request",
            RequestOrigin.LocalUi, () => true, f.Token);
        var gate = new TaskCompletionSource<OwnedInstructionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        sources.Pending = gate.Task;
        var resolving = workflow.ResolveAsync(review, RequestOrigin.LocalUi, f.Token);
        sources.Reads.Should().Be(1);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var observeCancellation = sources.ReadToken.Register(() => cancellationObserved.TrySetResult());
        var stopping = workflow.RevokeAsync(review);
        resolving.IsCompleted.Should().BeFalse();
        stopping.IsCompleted.Should().BeFalse();
        reader.Released.Should().BeFalse();
        await cancellationObserved.Task.WaitAsync(f.Token);
        gate.SetResult(sources.Sources[0]);
        await resolving.Invoking(value => value).Should().ThrowAsync<OperationCanceledException>();
        await stopping;
        reader.Released.Should().BeTrue();
        adapter.Calls.Should().Be(0);
        f.Count("conversation_messages").Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReferencedAgentWithdrawalCancelsAndJoinsIgnoringModelWithoutPublishingLateAnswer(bool combined)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var adapter = new IgnoringAgentAdapter();
        await using var composed = new Composition(f, adapter: adapter);
        var sources = new AgentOwnedStore();
        var revisions = new InstructionRevisionCatalogue(f.Paths);
        var catalogue = EnabledAgentInstructions(sources, revisions);
        var reader = ReferencedAgentReader(combined);
        await using var workflow = new BoundedAgentWorkflow(reader, f.Store, composed.Access, composed.Conversation,
            composed.Turns, composed.Audit, f.Time, new(catalogue, sources, revisions));
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        using var review = await workflow.ReviewAsync("selected.agent.md",
            (await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token))!, "retained original request",
            RequestOrigin.LocalUi, () => true, f.Token);
        await workflow.ResolveAsync(review, RequestOrigin.LocalUi, f.Token);
        var running = workflow.InvokeAsync(review, RequestOrigin.LocalUi, f.Token);
        await adapter.Started.Task;
        revisions.DisableExact(sources.Sources[0], catalogue);
        var stopping = workflow.RevokeAsync(review);
        running.IsCompleted.Should().BeFalse();
        stopping.IsCompleted.Should().BeFalse();
        reader.Released.Should().BeFalse();
        adapter.Reply.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("unauthorized late answer", null)));
        await running.Invoking(value => value).Should().ThrowAsync<Exception>();
        await stopping;
        reader.Released.Should().BeTrue();
        composed.Turns.IsQuiescent.Should().BeTrue();
        var message = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
        message.Body.Should().Be("retained original request");
        message.AssistantResponse.Should().BeNull();
        message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        composed.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, "agents.revision.invoke", StringComparison.Ordinal)
            && item.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
    }

    private static NativeAgentReader ReferencedAgentReader(bool combined = false)
    {
        var reader = new NativeAgentReader();
        reader.Text = reader.Text.Replace("kora-agent-format: 1", "kora-agent-format: 2", StringComparison.Ordinal)
            .Replace("references: none", "references: disk.kora-profile.instruction.example", StringComparison.Ordinal);
        if (combined)
        {
            reader.Text = reader.Text.Replace("kora-agent-format: 2", "kora-agent-format: 4", StringComparison.Ordinal)
                .Replace("tools: none", "tools: application.get_version", StringComparison.Ordinal)
                .Replace("maximum-calls: 0", "maximum-calls: 1", StringComparison.Ordinal);
        }
        return reader;
    }

    private static ArtifactCatalogue EnabledAgentInstructions(AgentOwnedStore sources, InstructionRevisionCatalogue revisions)
    {
        var catalogue = new ArtifactCatalogue(revisions.Reconcile(sources.Sources.Select(item =>
            item.Document.Definition with { OwnedInstructionRevision = item.Revision }).ToArray()));
        foreach (var item in sources.Sources)
        { revisions.CompleteActivation(item, revisions.PrepareActivation(item), catalogue); }
        return catalogue;
    }

    private sealed class IgnoringAgentAdapter : IModelTurnAdapter
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<ModelProviderReply> Reply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken token)
        {
            Started.SetResult();
            return Reply.Task;
        }
    }

    private sealed class NativeAgentReader : IAgentProfileReader, IAgentProfileSelection
    {
        public bool Released { get; private set; }
        public LocalFileMetadata Metadata { get; } = new(@"C:\Users\owner\existing.agent.md", "native-source", 512, DateTimeOffset.UnixEpoch);
        public Task<IAgentProfileSelection> OpenAsync(string path, CancellationToken token) => Task.FromResult<IAgentProfileSelection>(this);
        public Task<byte[]> ReadAsync(CancellationToken token) => Task.FromResult(Encoding.UTF8.GetBytes(Text));
        internal string Text { get; set; } = """
            ---
            name: existing-profile
            description: User role
            kora-agent-format: 1
            runtime: local
            input: text
            output: text
            references: none
            tools: none
            maximum-calls: 0
            maximum-rounds: 1
            maximum-input-bytes: 32768
            maximum-output-bytes: 4096
            deadline-ms: 15000
            ---
            Explain the original request. No scripts or imported references.
            """;
        public void Dispose() => Released = true;
    }

    private sealed class AgentOwnedStore : IOwnedInstructionStore
    {
        internal OwnedInstructionSnapshot[] Sources { get; } = new[] { "example", "second" }.Select(name =>
            new OwnedInstructionSnapshot(name, new string('a', 48), new string('b', 48),
                ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                    $"---\nkora-format: 1\nname: {name}\ndescription: Owned\n---\nExact owned {name} guidance.", false),
                    ArtifactKind.Instruction, "kora-profile", name))).ToArray();
        internal int Reads { get; private set; }
        internal CancellationToken ReadToken { get; private set; }
        internal Task<OwnedInstructionSnapshot?>? Pending { get; set; }
        public Task<OwnedInstructionSnapshot?> Read(string name, CancellationToken token)
        {
            Reads++;
            ReadToken = token;
            return Pending ?? Task.FromResult<OwnedInstructionSnapshot?>(Sources.Single(item => string.Equals(item.Name, name, StringComparison.Ordinal)));
        }
        public Task<IReadOnlyList<string>> List(CancellationToken token) => throw new InvalidOperationException("No discovery.");
        public Task<InstructionWriteResult> Write(string name, OwnedInstructionSnapshot? expected,
            ArtifactFileDocument? replacement, Func<bool> eligible,
            Func<Func<InstructionWriteResult>, Task<InstructionWriteResult>> commit, CancellationToken token) =>
            throw new InvalidOperationException("No writes.");
    }
}
