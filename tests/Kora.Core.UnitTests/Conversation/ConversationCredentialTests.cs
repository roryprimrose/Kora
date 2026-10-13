using System.Text;

using AwesomeAssertions;
using Kora.Core.Conversation;

namespace Kora.Core.UnitTests.Conversation;

public sealed class ConversationCredentialTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public void DeterministicStructuralFamiliesRejectWithoutDisclosingCandidate(int family)
    {
        var text = Candidate(family);
        var action = () => ConversationText.Validate(text);
        var error = action.Should().Throw<ConversationCredentialException>().Which;
        // Assertions must not render a candidate even when the scanner regresses.
        error.Message.Contains(text, StringComparison.Ordinal).Should().BeFalse();
        error.Message.Should().Contain("best-effort");
    }

    [Fact]
    public void AllDeclaredPrivateKeyLabelsAndMalformedStructuralCandidatesFailClosed()
    {
        foreach (var label in new[] { "PRIVATE KEY", "RSA PRIVATE KEY", "EC PRIVATE KEY", "DSA PRIVATE KEY", "OPENSSH PRIVATE KEY" })
        {
            var body = "-----BEGIN " + label + "-----\n" + new string('A', 64) + "\n-----END " + label + "-----";
            var action = () => ConversationText.Validate(body);
            action.Should().Throw<ConversationCredentialException>();
        }
        var incomplete = () => ConversationText.Validate("-----BEGIN PRIVATE KEY-----\n" + new string('A', 64));
        incomplete.Should().Throw<ConversationCredentialException>();
        var shortDescription = () => ConversationText.Validate("-----BEGIN PRIVATE KEY----- is a marker.");
        shortDescription.Should().NotThrow();
        var mixed = () => ConversationText.Validate("-----BEGIN PRIVATE KEY-----\n<redacted>\n-----END PRIVATE KEY-----\n"
            + "-----BEGIN PRIVATE KEY-----\n" + new string('A', 64) + "\n-----END PRIVATE KEY-----");
        mixed.Should().Throw<ConversationCredentialException>();
        var malformed = () => ConversationText.Validate("-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----");
        malformed.Should().Throw<ConversationCredentialException>();
    }

    [Theory]
    [InlineData("Passwords, client_secret and api_key are discussed; no value is assigned.")]
    [InlineData("Authorization headers use Bearer or Basic schemes.")]
    [InlineData("  original e\u0301🙂\r\nwhitespace  ")]
    [InlineData("password = <password>")]
    [InlineData("api_key = ${input:key}")]
    [InlineData("client_secret = ${env:SECRET_VALUE}")]
    [InlineData("api_key=example-value")]
    [InlineData("api_key=example_value")]
    [InlineData("password=sample-value")]
    [InlineData("password=dummy-value")]
    [InlineData("password=fake-value")]
    [InlineData("password=your_value")]
    [InlineData("password=your-value")]
    [InlineData("password=not-a-real-value")]
    [InlineData("password=********")]
    [InlineData("password=\"\"")]
    [InlineData("password=\"...\"")]
    [InlineData("password=xxxx")]
    [InlineData("password=xxxxxxxx")]
    [InlineData("password=XXXXXXXX")]
    [InlineData("Authorization: Bearer ${input:long_placeholder_parameter}")]
    [InlineData("Authorization: Bearer <token>")]
    [InlineData("Authorization: Basic example")]
    [InlineData("Authorization: Basic abc")]
    [InlineData("Authorization: Bearer short")]
    [InlineData("-----BEGIN PRIVATE KEY-----\n<redacted>\n-----END PRIVATE KEY-----")]
    [InlineData("-----BEGIN PRIVATE KEY-----\n\n-----END PRIVATE KEY-----")]
    [InlineData("-----BEGIN PRIVATE KEY-----\n...\n-----END PRIVATE KEY-----")]
    public void DiscussionAndClearlyDeclaredPlaceholdersRemainExact(string text)
    {
        ConversationText.Validate(text);
        var draft = new SavedConversationDraft(new(Guid.NewGuid()), 1, text);
        draft.Body.AsSpan().SequenceEqual(text.AsSpan()).Should().BeTrue();
    }

    [Fact]
    public void EncodedPlaceholderAndMaskedStructuredTokenAreNotExpandedOrTreatedAsRealMaterial()
    {
        ConversationText.Validate("Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("username:password")));
        ConversationText.Validate("ghp_" + new string('x', 36));
        ConversationText.Validate("password: ${input:password}");
        ConversationText.Validate("Not an assignment: a random long alphanumeric value " + new string('q', 80));
        foreach (var value in new[] { "example", "sample", "placeholder", "redacted", "changeme", "password", "token", "user", "username", "secret", "api_key", "client_secret" })
        { ConversationText.Validate("api_key=" + value); }
    }

    internal static string Candidate(int family) => family switch
    {
        0 => "-----BEGIN PRIVATE KEY-----\n" + new string('A', 64) + "\n-----END PRIVATE KEY-----",
        1 => "Authorization: Bearer " + new string('b', 32),
        2 => "Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("syntheticuser:syntheticvalue")),
        3 => "password=synthetic-value-123",
        4 => """{"client_secret":"synthetic-value-123"}""",
        5 => "api_key='synthetic-value-123'",
        6 => "ghp_" + new string('a', 36),
        7 => "github_pat_" + new string('a', 22) + "_" + new string('b', 59),
        8 => "eyJ" + new string('a', 20) + ".eyJ" + new string('b', 20) + "." + new string('c', 32),
        9 => "Authorization: Basic " + new string('!', 16),
        10 => "Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("synthetic-without-colon")),
        11 => "Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(":syntheticvalue")),
        _ => "Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("syntheticuser:")),
    };
}
