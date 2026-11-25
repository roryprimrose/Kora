using System.Text;
using System.Text.Json;

using AwesomeAssertions;
using Microsoft.Copilot;
using Kora.Core.Dependencies;
using Kora.Windows.Dependencies;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class CopilotSdkTurnRuntimeTests
{
    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    public async Task SelectedNativeConsumptionAndExactFinalContentAreRequired(bool skill, bool preload, bool include)
    {
        var fixture = new Fixture { ToolMode = ModelToolMode.None };
        var selection = new CopilotCustomizationSelection(skill, "exact-source", "revision-1", "reviewed",
            "Reviewed instructions", "Use the exact reviewed native instructions.", "Use the exact reviewed native instructions.");
        var reviews = 0;
        fixture.Client.BeforeSend = async token =>
        {
            var configuration = fixture.Client.Configuration!;
            configuration.Agents.Selected.Should().Be(skill ? "kora-reviewed-skill" : selection.Name);
            var agent = configuration.Agents.Definitions!.Should().ContainSingle().Subject;
            agent.Name.Should().Be(configuration.Agents.Selected);
            agent.Tools.Should().BeEmpty();
            agent.Infer.Should().BeFalse();
            agent.Model.Should().BeNull();
            agent.ReasoningEffort.Should().BeNull();
            configuration.Tools.AvailableTools.Should().BeEmpty();
            configuration.Tools.ExcludedTools.Should().Equal("builtin:*", "mcp:*");
            configuration.Skills.Directories.Should().BeEmpty();
            configuration.Agents.Directories.Should().BeEmpty();
            var nativeContent = agent.Prompt;
#pragma warning disable MCPSDK001 // Exercises the configured native eager-preload callback, not source text in the user prompt.
            if (skill && preload)
            {
                var provider = configuration.Skills.Provider!;
                var descriptor = (await provider.ListSkillsAsync(token)).Should().ContainSingle().Subject;
                descriptor.Directory.Should().BeNull();
                descriptor.Name.Should().Be(selection.Name);
                agent.Skills.Should().Equal(selection.Name);
                nativeContent = await provider.ReadSkillAsync(agent.Skills![0], token);
            }
#pragma warning restore MCPSDK001
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.githubcopilot.com/chat/completions")
            {
                Headers = { Authorization = new("Bearer", "synthetic-credential") },
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    model = fixture.Profile.Model.Value,
                    messages = new[] { include ? nativeContent : "native-definition-was-not-consumed", fixture.Client.Prompt },
                })),
            };
#pragma warning disable MCPSDK001 // Same final physical-send boundary used by the pinned SDK client.
            var gate = (CopilotFinalRequestHandler)fixture.Options!.Models.RequestHandler!;
#pragma warning restore MCPSDK001
            using var response = await gate.SendFinalAsync(request, fixture.Client.SessionId, null, request.RequestUri!.AbsoluteUri, token);
        };
        using var root = fixture.Root();
        var result = await fixture.Runtime.RunSelectedAsync(fixture.Provenance, fixture.Profile, fixture.Context,
            selection, _ => ValueTask.FromResult(true), (transmission, _) =>
            {
                reviews++;
                Encoding.UTF8.GetString(transmission.Body.AsSpan()).Should().Contain(selection.Instructions);
                return ValueTask.FromResult(true);
            }, Token);
        var succeeds = include && (!skill || preload);
        result.Outcome.Should().Be(succeeds ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.DeniedEgress);
        reviews.Should().Be(succeeds ? 1 : 0);
        fixture.Network.Sends.Should().Be(succeeds ? 1 : 0);
        fixture.Runtime.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task SelectedSourceRevokedDuringFinalReviewNeverPhysicallySends()
    {
        var fixture = new Fixture { ToolMode = ModelToolMode.None };
        var current = true;
        var selection = new CopilotCustomizationSelection(false, "source", "revision", "reviewed",
            "Description", "Agent instruction", "Agent instruction");
        fixture.Client.BeforeSend = async token =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.githubcopilot.com/chat/completions")
            {
                Headers = { Authorization = new("Bearer", "synthetic") },
                Content = new StringContent(JsonSerializer.Serialize(new { model = fixture.Profile.Model.Value, messages = new[] { selection.SourceText } })),
            };
#pragma warning disable MCPSDK001 // Pinned final-request test seam.
            var gate = (CopilotFinalRequestHandler)fixture.Options!.Models.RequestHandler!;
#pragma warning restore MCPSDK001
            using var response = await gate.SendFinalAsync(request, fixture.Client.SessionId, null, request.RequestUri!.AbsoluteUri, token);
        };
        using var root = fixture.Root();
        var reply = await fixture.Runtime.RunSelectedAsync(fixture.Provenance, fixture.Profile, fixture.Context, selection,
            _ => ValueTask.FromResult(current), (_, _) => { current = false; return ValueTask.FromResult(true); }, Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
        fixture.Network.Sends.Should().Be(0);
        fixture.Runtime.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task CancellationIgnoringNativeSkillCallbackRetainsOwnershipUntilPhysicalJoin()
    {
        var fixture = new Fixture { ToolMode = ModelToolMode.None };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var block = false;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var selection = new CopilotCustomizationSelection(true, "source", "revision", "reviewed",
            "Description", "Reviewed instructions", "Reviewed instructions");
        fixture.Client.BeforeSend = async token =>
        {
            block = true;
#pragma warning disable MCPSDK001 // Exercises the owned native callback, deliberately ignoring cancellation inside its source seam.
            await fixture.Client.Configuration!.Skills.Provider!.ReadSkillAsync(selection.Name, token);
#pragma warning restore MCPSDK001
        };
        using var root = fixture.Root();
        var operation = fixture.Runtime.RunSelectedAsync(fixture.Provenance, fixture.Profile, fixture.Context,
            selection, async _ =>
            {
                if (block) { entered.TrySetResult(); await release.Task; }
                return true;
            }, (_, _) => ValueTask.FromResult(true), cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(Token);
            await cancellation.CancelAsync();
            fixture.Runtime.IsQuiescent.Should().BeFalse();
            operation.IsCompleted.Should().BeFalse();
        }
        finally { release.TrySetResult(); }
        (await operation).Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        fixture.Network.Sends.Should().Be(0);
        fixture.Runtime.IsQuiescent.Should().BeTrue();
    }
}
