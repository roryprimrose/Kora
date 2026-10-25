using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Application.Hosting;
using Kora.Core.Agents;
using Kora.Core.Context;
using Kora.Core.Artifacts;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("no-resolver")]
    [InlineData("source-before-resolution")]
    [InlineData("source-before-invocation")]
    [InlineData("disabled")]
    [InlineData("input")]
    [InlineData("wrong-review")]
    [InlineData("missing-source")]
    [InlineData("oversized-context")]
    [InlineData("target")]
    [InlineData("v1-resolution")]
    public async Task AgentReferencedWorkflowRequiresSeparateConsentAndPreservesPortableRetainedOperation(string scenario)
    {
        var f = await Fixture.CreateInitializedAsync();
        var store = new SessionCommandStore(f);
        var workspace = new SessionWorkspaceService(store, new(f.HostStore), store, NullLogger<SessionWorkspaceService>.Instance);
        var model = new SpeechSuggestionModel();
        var conversation = new SessionConversationService(store, store, workspace, store, model, TimeProvider.System,
            NullLogger<SessionConversationService>.Instance, execution: model);
        var instructions = new Kora.Application.UnitTests.Agents.AgentInstructionReferenceResolverTests.Fixture();
        var reader = new ReferencedProfileReader();
        if (scenario is "v1-resolution")
        {
            reader.Text = reader.Text.Replace("kora-agent-format: 2", "kora-agent-format: 1", StringComparison.Ordinal)
                .Replace("references: disk.kora-profile.instruction.example", "references: none", StringComparison.Ordinal);
        }
        if (scenario is "oversized-context")
        { reader.Text = reader.Text.Replace("maximum-input-bytes: 32768", "maximum-input-bytes: 100", StringComparison.Ordinal); }
        await using var workflow = new BoundedAgentWorkflow(reader, store, store, conversation, model, f.Audit, TimeProvider.System,
            scenario is "no-resolver" ? null : instructions.Resolver);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        if (scenario is "no-resolver")
        {
            await workflow.Invoking(value => value.ReviewAsync("role.agent.md", store.Session, "original", RequestOrigin.LocalUi,
                () => true, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
            reader.Released.Should().BeTrue();
            return;
        }
        var current = true;
        using var review = await workflow.ReviewAsync("role.agent.md", store.Session, "original",
            RequestOrigin.LocalUi, () => current, TestContext.Current.CancellationToken);
        var profileRevision = review.SourceRevision;
        if (scenario is "v1-resolution")
        {
            await workflow.Invoking(value => value.ResolveAsync(review, RequestOrigin.LocalUi, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
            return;
        }
        review.IsInvocationReady.Should().BeFalse();
        instructions.Store.Reads.Should().Be(0);
        await workflow.Invoking(value => value.InvokeAsync(review, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        await workflow.Invoking(value => value.InvokeAsync(review, RequestOrigin.LocalUi, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        if (scenario is "source-before-resolution") { reader.Text += "\nChanged"; }
        if (scenario is "missing-source") { instructions.Store.Value = null; }
        if (scenario is "target") { instructions.Store.BeforeRead = store.ChangeAgentTarget; }
        if (scenario is "source-before-resolution" or "missing-source" or "oversized-context" or "target")
        {
            await workflow.Invoking(value => value.ResolveAsync(review, RequestOrigin.LocalUi, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<Exception>();
            reader.Released.Should().BeTrue();
            store.RequestRetentions.Should().Be(0);
            model.Calls.Should().Be(0);
            return;
        }
        await workflow.ResolveAsync(review, RequestOrigin.LocalUi, TestContext.Current.CancellationToken);
        review.IsInvocationReady.Should().BeTrue();
        review.SourceRevision.Should().NotBe(profileRevision);
        model.Calls.Should().Be(0);
        store.RequestRetentions.Should().Be(0);
        if (scenario is "wrong-review")
        {
            await using var other = new BoundedAgentWorkflow(new ReferencedProfileReader(), store, store, conversation,
                model, f.Audit, TimeProvider.System, instructions.Resolver);
            await other.Invoking(value => value.ResolveAsync(review, RequestOrigin.LocalUi, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
            return;
        }
        if (scenario is "source-before-invocation") { reader.Text += "\nChanged"; }
        if (scenario is "disabled") { instructions.Revisions.DisableExact(instructions.Store.Value!, instructions.Catalogue); }
        if (scenario is "input") { current = false; }
        if (scenario is not "success")
        {
            await workflow.Invoking(value => value.InvokeAsync(review, RequestOrigin.LocalUi, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<Exception>();
            model.Calls.Should().Be(0);
            store.RequestRetentions.Should().Be(0);
            return;
        }
        var receipt = await workflow.InvokeAsync(review, RequestOrigin.LocalUi, TestContext.Current.CancellationToken);
        receipt.Message.Body.Should().Be("original");
        receipt.Message.AssistantResponse!.Body.Should().Be("set speech.summary-words to 40");
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        reader.Released.Should().BeTrue();
        model.Calls.Should().Be(1);
        store.RequestRetentions.Should().Be(1);
    }

    private sealed partial class SessionCommandStore
    {
        internal void ChangeAgentTarget() => Session = Session with { Authority = Session.Authority with { IsActive = false } };
    }

    private sealed class ReferencedProfileReader : IAgentProfileReader, IAgentProfileSelection
    {
        internal bool Released { get; private set; }
        internal string Text { get; set; } = """
            ---
            name: role
            description: Role
            kora-agent-format: 2
            runtime: local
            input: text
            output: text
            references: disk.kora-profile.instruction.example
            tools: none
            maximum-calls: 0
            maximum-rounds: 1
            maximum-input-bytes: 32768
            maximum-output-bytes: 4096
            deadline-ms: 15000
            ---
            Exact role guidance.
            """;
        public LocalFileMetadata Metadata { get; } = new(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "owner", "role.agent.md")),
            "profile", 512, DateTimeOffset.UnixEpoch);
        public Task<IAgentProfileSelection> OpenAsync(string path, CancellationToken token) => Task.FromResult<IAgentProfileSelection>(this);
        public Task<byte[]> ReadAsync(CancellationToken token) => Task.FromResult(ArtifactFileDocument.Encode(Text, false));
        public void Dispose() => Released = true;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AgentCommandsPreserveOriginalChannelAndNeverUseLegacyPromptReasoner(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.SetAssistantNameAsync("Nova", Kora.Core.Auditing.SecurityAuditInitiator.LocalUser);
        var received = new List<AgentRunCommand>();
        f.ViewModel.AgentRunRequested += (command, origin, eligible) =>
        {
            eligible().Should().BeTrue();
            origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
            HostActivity.RequireCurrent().Request.Origin.Should().Be(origin);
            received.Add(command);
            return Task.CompletedTask;
        };
        foreach (var text in new[] { "review agent", "agent source C:\\role.agent.md", "agent request task",
            "agent targets", "agent next targets", "agent target exact-id", "agent review invocation",
            "agent review references", "agent consent resolution", "agent confirm invocation", "agent stop" })
        {
            if (voice) { await f.RaiseActivatedTranscriptAsync("Nova, " + text, 1); }
            else { await f.RunAsync(text); }
        }
        received.Select(item => item.Action).Should().Equal("Open", "Source", "Request", "Targets",
            "NextTargets", "Target", "Review", "Review", "Resolve", "Invoke", "Stop");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AgentUnboundOrNonoriginalInputCannotDispatch()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("review agent");
        f.ViewModel.Transcript.Should().Contain("unavailable");
        var calls = 0;
        f.ViewModel.AgentRunRequested += (_, _, _) => { calls++; return Task.CompletedTask; };
        await f.RaiseActivatedTranscriptAsync("agent confirm invocation", 1);
        using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request))
        { await f.RunAsync("agent confirm invocation"); }
        calls.Should().Be(0);
    }

    [Fact]
    public async Task AgentCannotReplacePendingQuestion()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "ready");
        f.Reasoner.Question = new("Original question?", ["one", "two"]);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("do the work");
        await f.ViewModel.ActiveReasoningTask!;
        var calls = 0;
        f.ViewModel.AgentRunRequested += (_, _, _) => { calls++; return Task.CompletedTask; };
        await f.RunAsync("agent confirm invocation");
        calls.Should().Be(0);
        f.ViewModel.IsResponseInteractionPending.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AgentShutdownJoinsBoundOwnershipOnly(bool bound)
    {
        var f = await Fixture.CreateInitializedAsync();
        var calls = 0;
        if (bound) { f.ViewModel.BindAgentShutdown(() => { calls++; return Task.CompletedTask; }); }
        await f.ViewModel.ExitAsync();
        calls.Should().Be(bound ? 1 : 0);
    }

    [Fact]
    public async Task IndependentRunCommandsPreserveOriginalGuardAndJoinTheirOwnShutdown()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.SetAssistantNameAsync("Nova", Kora.Core.Auditing.SecurityAuditInitiator.LocalUser);
        await f.RunAsync("independent agents");
        f.ViewModel.Transcript.Should().Contain("unavailable");
        var received = new List<RequestOrigin>();
        f.ViewModel.AgentRunSetRequested += (command, origin, eligible) =>
        {
            command.Action.Should().Be("Open");
            eligible().Should().BeTrue();
            HostActivity.RequireCurrent().Request.Origin.Should().Be(origin);
            received.Add(origin);
            return Task.CompletedTask;
        };
        await f.RunAsync("independent agents");
        await f.RaiseActivatedTranscriptAsync("Nova, independent agents", 1);
        using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Application, HostOperation.Request))
        { await f.RunAsync("independent agents"); }
        received.Should().Equal(RequestOrigin.LocalUi, RequestOrigin.ActivatedVoice);
        var closed = 0;
        f.ViewModel.BindAgentRunSetShutdown(() => { closed++; return Task.CompletedTask; });
        await f.ViewModel.ExitAsync();
        closed.Should().Be(1);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AgentUnavailableActivationNameCannotCreateAuthority()
    {
        var f = new Fixture();
        f.NamePreferences.Name = "Supported Commands";
        await f.ViewModel.InitializeAsync();
        var calls = 0;
        f.ViewModel.AgentRunRequested += (_, _, _) => { calls++; return Task.CompletedTask; };
        await f.RunAsync("Kora, review agent");
        calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
