using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(ModelTurnOutcome.Succeeded, SecurityAuditOutcome.Succeeded, HostOperationOutcome.Completed)]
    [InlineData(ModelTurnOutcome.Denied, SecurityAuditOutcome.Denied, HostOperationOutcome.Failed)]
    [InlineData(ModelTurnOutcome.DeniedEgress, SecurityAuditOutcome.Denied, HostOperationOutcome.Failed)]
    [InlineData(ModelTurnOutcome.Cancelled, SecurityAuditOutcome.Cancelled, HostOperationOutcome.Cancelled)]
    [InlineData(ModelTurnOutcome.Unknown, SecurityAuditOutcome.Unknown, HostOperationOutcome.Unknown)]
    [InlineData(ModelTurnOutcome.Unavailable, SecurityAuditOutcome.Failed, HostOperationOutcome.Failed)]
    [InlineData(ModelTurnOutcome.AuthenticationRequired, SecurityAuditOutcome.Failed, HostOperationOutcome.Failed)]
    [InlineData(ModelTurnOutcome.QuotaExceeded, SecurityAuditOutcome.Failed, HostOperationOutcome.Failed)]
    [InlineData(ModelTurnOutcome.TimedOut, SecurityAuditOutcome.Failed, HostOperationOutcome.Failed)]
    public async Task AgentInvocationAuditPreservesOriginalVoiceAndExactRetainedTerminal(
        ModelTurnOutcome outcome, SecurityAuditOutcome expectedAudit, HostOperationOutcome expectedActivity)
    {
        using var f = new Fixture(RequestOrigin.ActivatedVoice);
        f.Adapter.Reply = new(outcome, ModelProviderSelection.OllamaCandidate,
            outcome == ModelTurnOutcome.Succeeded ? new("answer", null) : null);
        var host = QualifiedHistoryHost(f);
        var reader = ToolFreeAuditAgent();
        await using var workflow = new BoundedAgentWorkflow(reader, f, f, f.ConversationService(host, execution: host),
            host, f, f.Time);
        using var root = f.Root();
        Activity? terminal = null;
        var stopped = new List<Activity>();
        listener.ActivityStopped = stopped.Add;
        f.OnAudit = entry =>
        {
            if (string.Equals(entry.ActionId, "agents.revision.invoke", StringComparison.Ordinal)
                && entry.Outcome != SecurityAuditOutcome.Requested)
            { terminal = HostActivity.RequireCurrent().Activity; }
        };
        using var review = await workflow.ReviewAsync("selected.agent.md", new(f.Session, null),
            "original voice task", RequestOrigin.ActivatedVoice, () => true, Token);
        f.Audits.Should().NotContain(entry => entry.ActionId == "agents.revision.invoke");
        f.RetainCalls.Should().Be(0);

        using var confirmation = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var receipt = await workflow.InvokeAsync(review, RequestOrigin.LocalUi, Token);

        receipt.Message.Outcome.Should().Be(outcome);
        receipt.Message.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        var audits = f.Audits.Where(entry => string.Equals(entry.ActionId, "agents.revision.invoke", StringComparison.Ordinal)).ToArray();
        audits.Select(entry => entry.Outcome).Should().Equal(SecurityAuditOutcome.Requested, expectedAudit);
        audits.Should().OnlyContain(entry => entry.Initiator == SecurityAuditInitiator.VoiceCommand
            && entry.CorrelationId == receipt.Message.Request.RequestId.Value
            && entry.TargetId == "agent-revision." + review.SourceRevision);
        audits[1].ReasonCode.Should().Be("retained-terminal");
        terminal.Should().NotBeNull();
        terminal!.IsStopped.Should().BeTrue();
        terminal.GetTagItem("kora.outcome").Should().Be(expectedActivity.ToString());
        stopped[^1].GetTagItem("kora.outcome").Should().Be(expectedActivity.ToString());
        f.Adapter.Calls.Should().Be(1);
        f.RetainCalls.Should().Be(1);
        reader.Released.Should().BeTrue();
        host.IsQuiescent.Should().BeTrue();
        if (outcome != ModelTurnOutcome.Succeeded)
        {
            receipt.Message.AssistantResponse.Should().BeNull();
            receipt.Message.Observations.Should().BeNull();
        }
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser)]
    [InlineData(RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand)]
    public async Task UnavailableAgentInvocationAuditsOriginalChannelWithoutProviderDispatch(
        RequestOrigin origin, SecurityAuditInitiator expectedInitiator)
    {
        using var f = new Fixture(origin);
        var host = f.ConfiguredProfiles(production: true);
        var reader = ToolFreeAuditAgent();
        await using var workflow = new BoundedAgentWorkflow(reader, f, f, f.ConversationService(host, execution: host),
            host, f, f.Time);
        using var root = f.Root();
        using var review = await workflow.ReviewAsync("selected.agent.md", new(f.Session, null), "original task",
            origin, () => true, Token);

        using var confirmation = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var receipt = await workflow.InvokeAsync(review, RequestOrigin.LocalUi, Token);

        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        receipt.Message.Request.Origin.Should().Be(origin);
        receipt.Message.AssistantResponse.Should().BeNull();
        var audits = f.Audits.Where(entry => string.Equals(entry.ActionId, "agents.revision.invoke", StringComparison.Ordinal)).ToArray();
        audits.Select(entry => entry.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Failed);
        audits.Should().OnlyContain(entry => entry.Initiator == expectedInitiator
            && entry.CorrelationId == receipt.Message.Request.RequestId.Value);
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(1);
        reader.Released.Should().BeTrue();
        host.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task AgentTerminalAuditFailureCannotReturnSuccessOrReportKnownFailureForRetainedAnswer()
    {
        using var f = new Fixture(RequestOrigin.ActivatedVoice);
        var host = QualifiedHistoryHost(f);
        var reader = ToolFreeAuditAgent();
        await using var workflow = new BoundedAgentWorkflow(reader, f, f, f.ConversationService(host, execution: host),
            host, f, f.Time);
        using var root = f.Root();
        f.OnAudit = entry =>
        {
            if (string.Equals(entry.ActionId, "agents.revision.invoke", StringComparison.Ordinal)
                && entry.Outcome == SecurityAuditOutcome.Succeeded)
            { throw new IOException("Terminal audit acknowledgement unavailable."); }
        };
        using var review = await workflow.ReviewAsync("selected.agent.md", new(f.Session, null), "original task",
            RequestOrigin.ActivatedVoice, () => true, Token);

        using var confirmation = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        await workflow.Invoking(value => value.InvokeAsync(review, RequestOrigin.LocalUi, Token))
            .Should().ThrowAsync<IOException>();

        f.Retained!.AssistantResponse!.Body.Should().Be("answer");
        f.Audits.Last().ActionId.Should().Be("agents.revision.invoke");
        f.Audits.Last().Outcome.Should().Be(SecurityAuditOutcome.Unknown);
        f.Audits.Last().Initiator.Should().Be(SecurityAuditInitiator.VoiceCommand);
        f.Adapter.Calls.Should().Be(1);
        reader.Released.Should().BeTrue();
        host.IsQuiescent.Should().BeTrue();
        await workflow.Invoking(value => value.InvokeAsync(review, RequestOrigin.LocalUi, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Adapter.Calls.Should().Be(1);
    }

    private static AgentReader ToolFreeAuditAgent() => new()
    {
        Text = AgentProfile.Replace("tools: application.get_version", "tools: none", StringComparison.Ordinal)
            .Replace("maximum-calls: 1", "maximum-calls: 0", StringComparison.Ordinal),
    };

    [Theory]
    [InlineData(false, HostOperationOutcome.Failed)]
    [InlineData(true, HostOperationOutcome.Cancelled)]
    public async Task AgentPreDispatchFailureReleasesSourceWithoutInventingInvocationAudit(
        bool cancelled, HostOperationOutcome expected)
    {
        using var f = new Fixture();
        var host = QualifiedHistoryHost(f);
        var reader = ToolFreeAuditAgent();
        await using var workflow = new BoundedAgentWorkflow(reader, f, f, f.ConversationService(host, execution: host),
            host, f, f.Time);
        using var root = f.Root();
        using var review = await workflow.ReviewAsync("selected.agent.md", new(f.Session, null), "original task",
            RequestOrigin.LocalUi, () => true, Token);
        reader.BeforeRead = () => Task.FromException(cancelled
            ? new OperationCanceledException(Token) : new IOException("Source unavailable."));
        var stopped = new List<Activity>();
        listener.ActivityStopped = stopped.Add;

        await workflow.Invoking(value => value.InvokeAsync(review, RequestOrigin.LocalUi, Token))
            .Should().ThrowAsync<Exception>();

        stopped[^1].GetTagItem("kora.outcome").Should().Be(expected.ToString());
        f.Audits.Should().NotContain(entry => entry.ActionId == "agents.revision.invoke");
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
        reader.Released.Should().BeTrue();
        host.IsQuiescent.Should().BeTrue();
    }
}
