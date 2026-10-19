using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Context;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Fact]
    public async Task FileAnswerWrapperUsesExistingHostAdmissionAndExecutionWithExplicitLocalChoice()
    {
        using var f = new Fixture();
        using var root = f.Root();
        await Configure(f, ModelProviderMode.HostedPreferred);
        var bytes = Encoding.UTF8.GetBytes("alpha exact marker");
        var source = new LocalFileRevision(new(Guid.NewGuid(), Guid.NewGuid(), f.Request,
            new(@"C:\Team\guide.md", "identity", bytes.Length, DateTimeOffset.UnixEpoch)), bytes, DateTimeOffset.UnixEpoch);
        var search = new LocalFileLexicalRetrieval().Search(source, source.Reference, "alpha", f.Time.GetUtcNow(), Token);
        var binding = new LocalFileAnswerContext(source, search, "alpha?", f.Request, new(1), f.Time.GetUtcNow());
        f.Adapter.OnRun = () =>
        {
            using var wire = JsonDocument.Parse(f.Adapter.Wire!);
            var id = wire.RootElement.GetProperty("Evidence")[0].GetProperty("Id").GetProperty("Value").GetGuid();
            f.Adapter.Reply = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                new(JsonSerializer.Serialize(new { unsupported = false, claims = new[] { new { text = "Exact marker", evidenceIds = new[] { id } } } }), null));
        };
        var model = new LocalFileAnswerModel(f.PolicyHost);
        var result = await model.RunAsync(binding.Envelope, () => true, Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        result.Provenance!.Selection.Should().Be(ModelProviderSelection.OllamaCandidate);
        f.Adapter.Calls.Should().Be(1);
        binding.Resolve(result, search).Claims.Single().Citations.Single().Should().BeSameAs(search.Citations.Single());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FileAnswerWrapperNeverDispatchesForUnavailableRuntimeOrSourceRevocation(bool revoke)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var host = revoke ? f.PolicyHost : f.Production();
        if (revoke) { await Configure(f); }
        var model = new LocalFileAnswerModel(host);
        var result = await model.RunAsync(f.Context(), () => !revoke, Token);
        result.Outcome.Should().Be(revoke ? ModelTurnOutcome.Denied : ModelTurnOutcome.Unavailable);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ProductionFileSourceUsePolicyIsExplicitlyUnavailableRatherThanPreviewConsent()
    {
        var policy = new UnavailableLocalFileModelUsePolicy();
        (await policy.AdmitAsync(null!, "question", null!, Token)).Should().Be(LocalFileModelUseDecision.Unavailable);
    }

    [Theory]
    [InlineData("admission")]
    [InlineData("execution")]
    public async Task FileAnswerWrapperFencesSourceAfterAdmissionAndExecution(string boundary)
    {
        using var f = new Fixture();
        using var root = f.Root();
        await Configure(f);
        var current = true;
        var checks = 0;
        if (string.Equals(boundary, "execution", StringComparison.Ordinal)) { f.Adapter.OnRun = () => current = false; }
        var model = new LocalFileAnswerModel(f.PolicyHost);
        var result = await model.RunAsync(f.Context(),
            () => current && (!string.Equals(boundary, "admission", StringComparison.Ordinal) || ++checks == 1), Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Denied);
        result.Response.Should().BeNull();
        f.Adapter.Calls.Should().Be(string.Equals(boundary, "execution", StringComparison.Ordinal) ? 1 : 0);
    }
}
