using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class ModelContextEnvelopeToolPolicyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SerializeUsesTheSoleExplicitEnvelopeToolPolicy(bool allowTools)
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var envelope = new ModelContextEnvelope(request, "policy", "user",
            [new(new(Guid.NewGuid()), request, new(1), "untrusted source", ModelEvidenceDisclosure.LocalOnly)],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(15),
            allowTools ? ModelToolMode.ReadOnly : ModelToolMode.None);
        using var json = JsonDocument.Parse(envelope.Serialize());
        envelope.ToolMode.Should().Be(allowTools ? ModelToolMode.ReadOnly : ModelToolMode.None);
        json.RootElement.GetProperty("Tools").GetArrayLength()
            .Should().Be(allowTools ? ReadOnlyCapabilityCatalog.Descriptors.Count : 0);
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