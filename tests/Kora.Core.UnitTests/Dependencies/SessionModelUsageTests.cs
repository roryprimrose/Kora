using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class SessionModelUsageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidateAcceptsContentFreeObservationsWithUnknownOrExactCounts(bool reported)
    {
        var usage = Usage() with
        {
            Model = new string('a', 256),
            TokenUsage = reported ? new(long.MaxValue, 0, 1, long.MaxValue) : null,
        };
        if (reported)
        {
            var request = usage.Provenance.Request;
            usage = usage with { Provenance = usage.Provenance with
            { Request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())) } };
        }
        usage.Validate();
        usage.Provenance.Request.SessionId.Value.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("provenance")]
    [InlineData("request")]
    [InlineData("selection")]
    [InlineData("provider")]
    [InlineData("model-id")]
    [InlineData("revision")]
    [InlineData("turn")]
    [InlineData("null-model")]
    [InlineData("empty-model")]
    [InlineData("blank-model")]
    [InlineData("oversized-model")]
    [InlineData("control-model")]
    [InlineData("elapsed")]
    [InlineData("observed")]
    [InlineData("counts")]
    public void ValidateRejectsMalformedObservationWithoutReinterpretingUnknownCounts(string fault)
    {
        var usage = Usage();
        usage = fault switch
        {
            "provenance" => usage with { Provenance = null! },
            "request" => usage with { Provenance = usage.Provenance with { Request = null! } },
            "selection" => usage with { Provenance = usage.Provenance with { Selection = null! } },
            "provider" => usage with { Provenance = usage.Provenance with { Selection = usage.Provenance.Selection with { Provider = (ModelProviderIdentity)99 } } },
            "model-id" => usage with { Provenance = usage.Provenance with { Selection = usage.Provenance.Selection with { Model = default } } },
            "revision" => usage with { Provenance = usage.Provenance with { Selection = usage.Provenance.Selection with { Revision = default } } },
            "turn" => usage with { Provenance = usage.Provenance with { Turn = default } },
            "null-model" => usage with { Model = null! },
            "empty-model" => usage with { Model = string.Empty },
            "blank-model" => usage with { Model = " " },
            "oversized-model" => usage with { Model = new string('a', 257) },
            "control-model" => usage with { Model = "model\nname" },
            "elapsed" => usage with { Elapsed = TimeSpan.FromTicks(-1) },
            "observed" => usage with { ObservedAt = default },
            "counts" => usage with { TokenUsage = new(InputTokens: -1) },
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
        Assert.Throws<InvalidDataException>(usage.Validate);
    }

    private static SessionModelUsage Usage() => new(
        new(new(Guid.NewGuid()), HostRequest.Create(RequestOrigin.LocalUi), ModelProviderSelection.OllamaCandidate),
        "host-model", new(), TimeSpan.Zero, new(2026, 11, 5, 0, 0, 0, TimeSpan.Zero));
}
