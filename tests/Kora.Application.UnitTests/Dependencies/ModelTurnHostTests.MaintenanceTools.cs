using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Maintenance;
using Kora.Core.Mcp;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("maintenance check latest", RequestOrigin.LocalUi)]
    [InlineData("maintenance upgrade 1.2.3", RequestOrigin.ActivatedVoice)]
    public async Task OriginalMaintenanceConversationSelectsOnlyTheBoundedLocalMaintenanceContext(string body, RequestOrigin origin)
    {
        using var f = new Fixture();
        var admission = new TestAdmission(ModelTurnOutcome.Unavailable);

        var receipt = await f.ConversationService(admission).SubmitAsync(new(f.Session, null), body,
            static () => true, Token, origin: origin);

        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        admission.Context!.UserRequest.Should().Be(body);
        admission.Context.Request.Origin.Should().Be(origin);
        admission.Context.ToolMode.Should().Be(ModelToolMode.ApplicationMaintenance);
        (admission.Context.ExpiresAt - admission.Context.CreatedAt).Should().Be(MaintenanceTools.Deadline);
        admission.Context.Tools.Should().Equal(MaintenanceTools.Descriptors);
        admission.Context.SystemPolicy.Should().Contain(MaintenanceTools.ModelPolicy);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitMaintenanceModelToolsUseTheBuiltInMcpAdmission(bool disabled)
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.Time.ExpectedDeadline = MaintenanceTools.Deadline;
        var store = new Tools.KoraMcpServerTests.MemoryStore();
        var directories = new LocalArtifactDirectoryPreferences(store);
        var preferences = new McpCapabilityPreferences(new ArtifactUsePreferences(store),
            directories, new ArtifactDirectoryConfigurationService(directories, f));
        if (disabled) { preferences.SetToolEnabled(KoraMcpCatalog.ServerId, MaintenanceTools.Upgrade, false); }
        var transport = new ToolTransport(
            """{"kind":"toolCalls","calls":[{"callId":"upgrade","capabilityId":"application.upgrade","input":{"version":"1.2.3"}}]}""",
            FinalAnswer);
        await using var loop = CreateLoop(f, transport);
        var maintenance = new MaintenanceModelRunner();
        var host = f.LoopHost(loop, preferences, maintenance: maintenance);
        var source = f.Context(toolMode: ModelToolMode.ApplicationMaintenance);
        var context = new ModelContextEnvelope(source.Request, source.SystemPolicy, source.UserRequest, source.Evidence,
            source.CreatedAt, source.CreatedAt.Add(MaintenanceTools.Deadline), source.ToolMode);
        var turn = (await host.AdmitAsync(ModelProviderSelection.OllamaCandidate, context, Token)).Turn!;

        var result = await host.RunAsync(turn, Token);

        maintenance.Calls.Should().Be(disabled ? 0 : 1);
        if (disabled) { result.Response.Should().BeNull(); }
        else
        {
            result.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
            transport.Requests.Should().HaveCount(2);
        }
    }

    [Theory]
    [InlineData(ModelToolMode.None)]
    [InlineData(ModelToolMode.ReadOnly)]
    public async Task OrdinaryAndToolFreeModelsCannotAcquireInstallerTools(ModelToolMode mode)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var transport = new ToolTransport(
            """{"kind":"toolCalls","calls":[{"callId":"upgrade","capabilityId":"application.upgrade","input":{}}]}""");
        await using var loop = CreateLoop(f, transport);
        var maintenance = new MaintenanceModelRunner();
        var host = f.LoopHost(loop, maintenance: maintenance);
        var turn = (await host.AdmitAsync(ModelProviderSelection.OllamaCandidate, f.Context(toolMode: mode), Token)).Turn!;

        var result = await host.RunAsync(turn, Token);

        result.Response.Should().BeNull();
        maintenance.Calls.Should().Be(0);
    }

    [Fact]
    public async Task HostedModelsCannotAdmitTheLocalMaintenanceScope()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var admission = await f.Host.AdmitAsync(ModelProviderSelection.CopilotCandidate,
            f.Context(toolMode: ModelToolMode.ApplicationMaintenance), Token);
        admission.Result.Reason.Should().Be(ModelTurnReason.InvalidPolicy);
        admission.Turn.Should().BeNull();
    }

    private sealed class MaintenanceModelRunner : IApplicationMaintenanceMcp
    {
        public int Calls { get; private set; }
        public ValueTask<CapabilityReply> InvokeAsync(string id, string input, Func<bool> admitted, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            admitted().Should().BeTrue();
            Calls++;
            return ValueTask.FromResult(new CapabilityReply(CapabilityOutcome.Succeeded, MaintenanceTools.ResultReason,
                Maintenance: new("1.0.0", ReleaseChannel.Production, ReleaseAvailability.Available,
                    "The exact installer was started; installation completion is not claimed.", "1.2.3", InstallerStartOutcome.Started)));
        }
    }
}
