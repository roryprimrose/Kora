using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Providers;

namespace Kora.Core.UnitTests.Providers;

public sealed class LocalModelQualificationEvidenceTests
{
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] Gates = ["licence-identity", "quality-authority", "bounds", "cancellation-recovery",
        "resource-latency", "offline-egress", "integrated-host"];
    private const string Template = """
        {"Format":1,"HostContract":"kora-ollama-native-v1","ProfileId":"664dcc7b-4dce-44fe-8f1a-b99a165a9e62","Revision":1,
        "Label":"Deterministic test only","RuntimeVersion":"fixture","ExecutableSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        "Model":"fixture:one","ModelSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","Reasoning":"off","Context":"bounded",
        "IssuedAt":"2026-10-23T12:00:00Z","ExpiresAt":"2026-10-23T13:00:00Z","Proofs":PROOFS}
        """;

    [Fact]
    public void ExactReleasePinnedCompleteEvidenceHasBoundedValidityAndNoImplicitOptions()
    {
        var bytes = Bytes();
        var evidence = LocalModelQualificationEvidence.Read(bytes, Hash(bytes));
        evidence.ProfileId.Should().Be(new Guid("664dcc7b-4dce-44fe-8f1a-b99a165a9e62"));
        evidence.Digest.Should().Be(Hash(bytes));
        evidence.Revision.Should().Be(1);
        evidence.Label.Should().Be("Deterministic test only");
        evidence.RuntimeVersion.Should().Be("fixture");
        evidence.Model.Should().Be("fixture:one");
        evidence.ExecutableSha256.Should().Be(Digest);
        evidence.ModelSha256.Should().Be(Digest);
        evidence.IssuedAt.Should().Be(Now);
        evidence.ExpiresAt.Should().Be(Now.AddHours(1));
        evidence.IsCurrent(Now).Should().BeTrue();
        evidence.IsCurrent(Now.AddMinutes(-1)).Should().BeFalse();
        evidence.IsCurrent(Now.AddHours(1)).Should().BeFalse();
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("oversize")]
    [InlineData("wrong-pin")]
    [InlineData("bad-pin")]
    [InlineData("malformed-json")]
    [InlineData("unknown-field")]
    [InlineData("missing-field")]
    [InlineData("duplicate-field")]
    [InlineData("root-type")]
    [InlineData("unknown-format")]
    [InlineData("host-contract")]
    [InlineData("reasoning")]
    [InlineData("context")]
    [InlineData("empty-id")]
    [InlineData("malformed-id")]
    [InlineData("revision")]
    [InlineData("expiry")]
    [InlineData("runtime-hash")]
    [InlineData("model-hash")]
    [InlineData("proof-type")]
    [InlineData("missing-proof")]
    [InlineData("duplicate-gate")]
    [InlineData("unknown-gate")]
    [InlineData("failed-proof")]
    [InlineData("bad-source")]
    [InlineData("bad-artifact")]
    [InlineData("bad-run")]
    [InlineData("null-label")]
    [InlineData("empty-label")]
    [InlineData("long-label")]
    [InlineData("control-label")]
    [InlineData("bad-model")]
    public void UntrustedIncompleteOrMalformedAttestationsNeverQualify(string fault)
    {
        var text = Encoding.UTF8.GetString(Bytes());
        text = fault switch
        {
            "malformed-json" => "{",
            "unknown-field" => text.Replace("\"Format\":1", "\"Extra\":1", StringComparison.Ordinal),
            "missing-field" => text.Replace("\"Format\":1,", "", StringComparison.Ordinal),
            "duplicate-field" => text.Replace("\"Format\":1", "\"Revision\":1", StringComparison.Ordinal),
            "root-type" => "[]",
            "unknown-format" => text.Replace("\"Format\":1", "\"Format\":2", StringComparison.Ordinal),
            "host-contract" => text.Replace("kora-ollama-native-v1", "unknown", StringComparison.Ordinal),
            "reasoning" => text.Replace("\"off\"", "\"on\"", StringComparison.Ordinal),
            "context" => text.Replace("\"bounded\"", "\"unlimited\"", StringComparison.Ordinal),
            "empty-id" => text.Replace("664dcc7b-4dce-44fe-8f1a-b99a165a9e62", Guid.Empty.ToString("D"), StringComparison.Ordinal),
            "malformed-id" => text.Replace("664dcc7b-4dce-44fe-8f1a-b99a165a9e62", "invalid", StringComparison.Ordinal),
            "revision" => text.Replace("\"Revision\":1", "\"Revision\":0", StringComparison.Ordinal),
            "expiry" => text.Replace("13:00:00Z", "12:00:00Z", StringComparison.Ordinal),
            "runtime-hash" => text.Replace("\"ExecutableSha256\":\"" + Digest, "\"ExecutableSha256\":\"bad", StringComparison.Ordinal),
            "model-hash" => text.Replace("\"ModelSha256\":\"" + Digest, "\"ModelSha256\":\"bad", StringComparison.Ordinal),
            "proof-type" => text[..text.IndexOf("\"Proofs\":", StringComparison.Ordinal)] + "\"Proofs\":{}}",
            "missing-proof" => text[..text.IndexOf("\"Proofs\":", StringComparison.Ordinal)] + "\"Proofs\":[]}",
            "duplicate-gate" => text.Replace("bounds", "licence-identity", StringComparison.Ordinal),
            "unknown-gate" => text.Replace("bounds", "unknown", StringComparison.Ordinal),
            "failed-proof" => text.Replace("passed", "failed", StringComparison.Ordinal),
            "bad-source" => text.Replace(new string('b', 40), "unknown", StringComparison.Ordinal),
            "bad-artifact" => text.Replace("\"ArtifactSha256\":\"" + Digest, "\"ArtifactSha256\":\"bad", StringComparison.Ordinal),
            "bad-run" => text.Replace("fixture-run", "bad run", StringComparison.Ordinal),
            "null-label" => text.Replace("\"Deterministic test only\"", "null", StringComparison.Ordinal),
            "empty-label" => text.Replace("Deterministic test only", "", StringComparison.Ordinal),
            "long-label" => text.Replace("Deterministic test only", new string('x', 257), StringComparison.Ordinal),
            "control-label" => text.Replace("Deterministic test only", @"bad\u0001", StringComparison.Ordinal),
            "bad-model" => text.Replace("fixture:one", "bad model", StringComparison.Ordinal),
            _ => text,
        };
        var bytes = fault is "empty" ? [] : fault is "oversize" ? new byte[LocalModelQualificationEvidence.MaximumBytes + 1] : Encoding.UTF8.GetBytes(text);
        var pin = fault is "wrong-pin" ? Digest : fault is "bad-pin" ? "untrusted" : Hash(bytes);
        var action = () => LocalModelQualificationEvidence.Read(bytes, pin);
        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void PersistedChoiceContainsNoProcessAuthorityAndRoundTripsOnlyStrictKnownFields()
    {
        var choice = new LocalModelAdmissionPreference(Digest, Guid.NewGuid(), new("account"), Now, Now.AddHours(1));
        var text = choice.Serialize();
        LocalModelAdmissionPreference.Parse(text).Should().Be(choice);
        text.ToLowerInvariant().Should().NotContain("process").And.NotContain("executable");
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"Format\":2,\"EvidenceDigest\":null,\"ProfileId\":null,\"Account\":null,\"ReviewedAt\":null,\"ExpiresAt\":null}")]
    public void UnknownSavedChoiceFormatsAreExplicitCorruption(string text)
    {
        var action = () => LocalModelAdmissionPreference.Parse(text);
        action.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("profile")]
    [InlineData("account")]
    [InlineData("reviewed")]
    [InlineData("expires")]
    public void InvalidSavedChoiceCannotBeSerialized(string field)
    {
        var value = new LocalModelAdmissionPreference(field is "digest" ? "bad" : Digest,
            field is "profile" ? Guid.Empty : Guid.NewGuid(), field is "account" ? null! : new("account"),
            field is "reviewed" ? DateTimeOffset.MinValue : Now, field is "expires" ? Now : Now.AddHours(1));
        var action = () => value.Serialize();
        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void SavedChoiceRejectsOversizeAndMalformedTypedFields()
    {
        var value = new LocalModelAdmissionPreference(Digest, Guid.NewGuid(), new("account"), Now, Now.AddHours(1)).Serialize();
        foreach (var text in new[] { new string('x', LocalModelAdmissionPreference.MaximumBytes + 1),
            value.Replace("\"ProfileId\":\"", "\"ProfileId\":\"bad", StringComparison.Ordinal),
            value.Replace("\"Account\":\"account\"", "\"Account\":3", StringComparison.Ordinal) })
        {
            var action = () => LocalModelAdmissionPreference.Parse(text);
            action.Should().Throw<InvalidDataException>();
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static byte[] Bytes()
    {
        var proofs = Gates.Select(gate => new
        {
            Gate = gate,
            SourceRevision = new string('b', 40),
            RunId = "fixture-run",
            ArtifactSha256 = Digest,
            Outcome = "passed",
        });
        return Encoding.UTF8.GetBytes(Template.Replace("PROOFS", JsonSerializer.Serialize(proofs), StringComparison.Ordinal));
    }
}