using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Core.UnitTests.Memory;

public sealed class MemorySessionCopyPolicyTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("scope")]
    [InlineData("origin")]
    [InlineData("id")]
    [InlineData("same")]
    [InlineData("revision")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("missing")]
    [InlineData("reference")]
    [InlineData("source-scope")]
    [InlineData("disabled")]
    [InlineData("value")]
    [InlineData("candidate")]
    public void CopySourceRequiresExactCurrentEnabledSessionDataWithoutInheritingReview(string fault)
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var boundary = new MemoryBoundary(new(Guid.NewGuid()), request.SessionId, new(1),
            null, null, null, 1, true, true, true);
        var source = new MemoryRecord(new(Guid.NewGuid()), new(3), MemoryScope.Session(request.SessionId),
            new(request, new(1), boundary.Profile, MemoryProposalOrigin.User, null, null),
            new(MemoryContentClass.ResponsePreference, "Prefer explicit examples"), DateTimeOffset.UnixEpoch,
            MemoryReviewState.Admitted, MemoryRetentionState.Enabled,
            new(request.RequestId, new(2), DateTimeOffset.UnixEpoch, boundary));
        var reference = new MemorySessionCopySource(source.Id, source.Revision, boundary.Session, boundary.Generation);
        var copy = source with
        {
            Id = new(Guid.NewGuid()), Revision = new(1), Scope = MemoryScope.DeviceProfile(boundary.Profile),
            Lineage = source.Lineage with { CopiedFromSession = reference }, Receipt = null,
            Review = MemoryReviewState.Proposed, Retention = MemoryRetentionState.Pending,
            Candidate = source.Candidate! with { ContentClass = MemoryContentClass.WorkflowPreference },
        };
        copy = fault switch
        {
            "scope" => copy with { Scope = source.Scope },
            "origin" => copy with { Lineage = copy.Lineage with { Origin = MemoryProposalOrigin.Model } },
            "id" => copy with { Lineage = copy.Lineage with { CopiedFromSession = reference with { Id = default } } },
            "same" => copy with { Id = source.Id },
            "revision" => copy with { Lineage = copy.Lineage with { CopiedFromSession = reference with { Revision = default } } },
            "session" => copy with { Lineage = copy.Lineage with { CopiedFromSession = reference with { Session = new(Guid.NewGuid()) } } },
            "generation" => copy with { Lineage = copy.Lineage with { CopiedFromSession = reference with { Generation = new(2) } } },
            "reference" => copy with { Lineage = copy.Lineage with { CopiedFromSession = reference with { Revision = new(4) } } },
            "value" => copy with { Candidate = copy.Candidate! with { Value = "replacement" } },
            "candidate" => copy with { Candidate = null },
            _ => copy,
        };
        source = fault switch
        {
            "source-scope" => source with { Scope = copy.Scope },
            "disabled" => source with { Retention = MemoryRetentionState.Disabled },
            _ => source,
        };
        MemoryPolicy.MatchesSessionCopySource(copy, fault is "missing" ? null : source, boundary)
            .Should().Be(fault is "none");
        if (fault is "scope" or "origin" or "id" or "same" or "revision" or "session" or "generation")
        { MemoryPolicy.CheckLineage(copy, boundary).Should().Be(MemoryReason.LineageUnknown); }
        source.Lineage.CopiedFromSession.Should().BeNull();
        JsonSerializer.Serialize(source.Lineage).Should().NotContain("CopiedFromSession");
    }
}
