using System.Security.Cryptography;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Providers;
using Kora.Definitions.Qualifications;

namespace Kora.Definitions.UnitTests;

public sealed class BundledLocalModelQualificationAuthorityTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly string[] Gates = ["licence-identity", "quality-authority", "bounds", "cancellation-recovery",
        "resource-latency", "offline-egress", "integrated-host"];

    [Fact]
    public async Task ShippedReleaseHasNoAcceptedProfileAndDoesNotInventEvidence()
    {
        (await new BundledLocalModelQualificationAuthority().Read(Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task OnlyExactReleasePinnedResourceBytesBecomeTrustedEvidence()
    {
        var bytes = Evidence();
        var digest = Convert.ToHexString(SHA256.HashData(bytes));
        var authority = new BundledLocalModelQualificationAuthority(new Dictionary<string, string>(StringComparer.Ordinal) { ["exact-resource"] = digest },
            name => name is "exact-resource" ? new MemoryStream(bytes, false) : null);
        var result = await authority.Read(Token);
        result.Should().ContainSingle().Which.Digest.Should().Be(digest);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("changed")]
    [InlineData("oversize")]
    [InlineData("duplicate-profile")]
    [InlineData("cancelled")]
    public async Task MissingChangedOversizeAmbiguousOrCancelledReleaseResourcesFailClosed(string fault)
    {
        var bytes = Evidence();
        var digest = Convert.ToHexString(SHA256.HashData(bytes));
        var pins = new Dictionary<string, string>(StringComparer.Ordinal) { ["first"] = digest };
        if (fault is "duplicate-profile") { pins["second"] = digest; }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (fault is "cancelled") { await cancellation.CancelAsync(); }
        var authority = new BundledLocalModelQualificationAuthority(pins, _ => fault is "missing" ? null : new MemoryStream(
            fault is "changed" ? [0] : fault is "oversize" ? new byte[LocalModelQualificationEvidence.MaximumBytes + 1] : bytes));
        var action = () => authority.Read(cancellation.Token);
        if (fault is "cancelled") { await action.Should().ThrowAsync<OperationCanceledException>(); }
        else { await action.Should().ThrowAsync<InvalidDataException>(); }
    }

    private static byte[] Evidence() => JsonSerializer.SerializeToUtf8Bytes(new
    {
        Format = 1,
        HostContract = LocalModelQualificationEvidence.HostContract,
        ProfileId = Guid.NewGuid(),
        Revision = 1,
        Label = "Deterministic fixture",
        RuntimeVersion = "fixture",
        ExecutableSha256 = new string('a', 64),
        Model = "fixture:one",
        ModelSha256 = new string('a', 64),
        Reasoning = "off",
        Context = "bounded",
        IssuedAt = DateTimeOffset.UnixEpoch,
        ExpiresAt = DateTimeOffset.UnixEpoch.AddHours(1),
        Proofs = Gates.Select(gate => new
        {
            Gate = gate,
            SourceRevision = new string('b', 40),
            RunId = "fixture-run",
            ArtifactSha256 = new string('c', 64),
            Outcome = "passed"
        }).ToArray(),
    });
}