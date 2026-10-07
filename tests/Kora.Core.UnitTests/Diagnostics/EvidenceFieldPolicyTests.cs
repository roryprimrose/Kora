using AwesomeAssertions;

using Kora.Core.Diagnostics;

namespace Kora.Core.UnitTests.Diagnostics;

public sealed class EvidenceFieldPolicyTests
{
    [Theory]
    [InlineData(EvidenceValueKind.Null, null)]
    [InlineData(EvidenceValueKind.Text, "")]
    [InlineData(EvidenceValueKind.Boolean, "true")]
    [InlineData(EvidenceValueKind.Boolean, "false")]
    [InlineData(EvidenceValueKind.WholeNumber, "-42")]
    [InlineData(EvidenceValueKind.Real, "1.25")]
    [InlineData(EvidenceValueKind.Identifier, "12345678-1234-1234-1234-123456789abc")]
    [InlineData(EvidenceValueKind.Timestamp, "2026-10-07T00:00:00.0000000+00:00")]
    public void Admitted_canonical_values_preserve_the_existing_storage_semantics(EvidenceValueKind kind, string? canonical) =>
        EvidenceFieldPolicy.ValidateValue(new(kind, canonical));

    [Theory]
    [InlineData((EvidenceValueKind)99, "")]
    [InlineData(EvidenceValueKind.Null, "x")]
    [InlineData(EvidenceValueKind.Text, null)]
    [InlineData(EvidenceValueKind.Boolean, "TRUE")]
    [InlineData(EvidenceValueKind.WholeNumber, "1.5")]
    [InlineData(EvidenceValueKind.WholeNumber, "x")]
    [InlineData(EvidenceValueKind.Real, "x")]
    [InlineData(EvidenceValueKind.Real, "Infinity")]
    [InlineData(EvidenceValueKind.Identifier, "x")]
    [InlineData(EvidenceValueKind.Timestamp, "x")]
    public void Invalid_canonical_values_fail_explicitly(EvidenceValueKind kind, string? canonical)
    {
        var invalid = () => EvidenceFieldPolicy.ValidateValue(new(kind, canonical));
        invalid.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Missing_and_oversize_values_are_rejected()
    {
        var missing = () => EvidenceFieldPolicy.ValidateValue(null!);
        missing.Should().Throw<ArgumentNullException>();
        var oversize = () => EvidenceFieldPolicy.ValidateValue(new(EvidenceValueKind.Text, new string('x', 1025)));
        oversize.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("Secret")]
    [InlineData("Token")]
    [InlineData("Path")]
    [InlineData("Transcript")]
    [InlineData("Content")]
    [InlineData("Arguments")]
    [InlineData("Response")]
    [InlineData("Speech")]
    public void Sensitive_field_names_share_capture_and_query_redaction_policy(string name) =>
        EvidenceFieldPolicy.IsSensitive(name).Should().BeTrue();

    [Fact]
    public void Ordinary_named_fields_are_not_sensitive() => EvidenceFieldPolicy.IsSensitive("Count").Should().BeFalse();
}
