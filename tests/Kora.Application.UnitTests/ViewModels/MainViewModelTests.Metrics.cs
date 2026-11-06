using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("typed")]
    [InlineData("voice")]
    [InlineData("exact")]
    [InlineData("speech")]
    [InlineData("no-target")]
    [InlineData("retired")]
    [InlineData("model")]
    [InlineData("invalid")]
    [InlineData("unprefixed")]
    [InlineData("native")]
    [InlineData("editor")]
    [InlineData("workspace-unbound")]
    [InlineData("unbound")]
    public async Task MetricsUseOriginalExactSessionWithoutModelExecutionOrRetainedRequest(string route)
    {
        var f = await Fixture.CreateInitializedAsync();
        var store = new SessionCommandStore(f);
        var workspace = new SessionWorkspaceService(store, new(f.HostStore), store, NullLogger<SessionWorkspaceService>.Instance);
        using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
        var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
        var registry = new ReadOnlyCapabilityRegistry(store, new(), new(), new(f.ApplicationInfo), new(dependencies),
            new(runtime), new(runtime), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
        await using var turns = new ModelTurnHost(store, store, store, registry, f.Audit,
            NullLogger<ModelTurnHost>.Instance, TimeProvider.System, []);
        await using var conversation = new SessionConversationService(store, store, workspace, store, turns, TimeProvider.System,
            NullLogger<SessionConversationService>.Instance, execution: turns);
        if (route is not "workspace-unbound") { f.ViewModel.BindSessionCommands(workspace); }
        f.ViewModel.BindRemainingWork(new(workspace, conversation, store, TimeProvider.System), conversation);
        if (route is "unbound") { f.ViewModel.BindSessionCommands(workspace); }
        var foreground = new ForegroundConversationTarget(store.Session, () => true);
        if (route is not "no-target") { f.ViewModel.PublishForegroundConversationTarget(foreground); }
        if (route is "retired") { foreground.Retire(); }
        if (route is "speech") { f.ViewModel.DefaultResponseMode = Kora.Core.Voice.ResponseOutputMode.Hybrid; }
        if (route is "editor") { f.ViewModel.PublishCustomizationVoiceTarget(EditorVoiceTarget()); }
        if (route is "model" or "native")
        {
            await f.ViewModel.ExecuteSessionCommandAsync(new(SessionCommandOperation.Metrics, store.Session.Authority.SessionId.Value),
                route is "model" ? SecurityAuditInitiator.ModelSuggestion : SecurityAuditInitiator.LocalUser);
        }
        else if (route is "voice" or "unprefixed" or "editor")
        { await f.RaiseActivatedTranscriptAsync((route is "voice" or "editor" ? "Kora, " : "") + "show session metrics", 1); }
        else
        {
            await f.RunAsync(route is "exact" ? "session metrics " + store.Session.Authority.SessionId.Value.ToString("D")
                : route is "invalid" ? "session metrics active" : "show session metrics");
        }
        if (route is "typed" or "voice" or "exact" or "speech" or "native")
        {
            f.ViewModel.ResponseTitle.Should().Be("Session model metrics.", f.ViewModel.Transcript);
            f.ViewModel.ResponseBody.Should().Contain("Finished model executions: 0").And.Contain("not reported");
            if (route is "speech") { f.TextToSpeech.SpokenText.Should().Contain("No finished model execution"); }
        }
        else { f.ViewModel.ResponseBody.Should().NotContain("Finished model executions"); }
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        store.RequestRetentions.Should().Be(0);
    }

    [Fact]
    public async Task MetricsNeverReplaceAnOutstandingQuestionOrApproval()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "Ready");
        f.Reasoner.Question = new("Which target?", ["One", "Two"]);
        await f.ViewModel.InitializeAsync();
        var (_, conversation) = BindRemainingWork(f);
        await using var owned = conversation;
        await f.RunAsync("choose");
        await f.ViewModel.ActiveReasoningTask!;
        var body = f.ViewModel.ResponseBody;
        await f.RunAsync("show session metrics");
        await f.RunAsync("session metrics active");
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        await f.ViewModel.ExecuteSessionCommandAsync(new(SessionCommandOperation.Metrics), SecurityAuditInitiator.LocalUser);
        f.ViewModel.ResponseBody.Should().Be(body);
        await f.ViewModel.CancelModelQuestionAsync();
        await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));
        body = f.ViewModel.ResponseBody;
        await f.RunAsync("show session metrics");
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.IsGrantChangePending.Should().BeTrue();
    }
}
