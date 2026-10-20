using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class ModelContextEnvelopeToolPolicyTests
{
    [Theory]
    [InlineData(ModelToolMode.ReadOnly)]
    [InlineData(ModelToolMode.None)]
    public void SerializeUsesTheSoleExplicitEnvelopeToolPolicy(ModelToolMode toolMode)
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var envelope = new ModelContextEnvelope(request, "policy", "user",
            [new(new(Guid.NewGuid()), request, new(1), "untrusted source", ModelEvidenceDisclosure.LocalOnly)],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(15), toolMode);
        using var json = JsonDocument.Parse(envelope.Serialize());
        envelope.ToolMode.Should().Be(toolMode);
        json.RootElement.GetProperty("ToolMode").GetInt32().Should().Be((int)toolMode);
        json.RootElement.GetProperty("Tools").GetArrayLength()
            .Should().Be(toolMode == ModelToolMode.ReadOnly ? ReadOnlyCapabilityCatalog.Descriptors.Count : 0);
        envelope.RequireCompleteInputLimit();
    }

    [Fact]
    public void ExistingEnvelopeCallersRetainTheirExactDefaultToolPolicy()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var envelope = new ModelContextEnvelope(request, "policy", "user", [],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(15));
        envelope.ToolMode.Should().Be(ModelToolMode.ReadOnly);
    }
}