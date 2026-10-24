using System.Text;
using AwesomeAssertions;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class ClaudeTransmissionTests
{
    private static ProviderProfileTuple Profile => new(ProviderService.Claude, new("lease"), new(1),
        new("exact-model"), new("disabled"), new("8192"));
    private static ModelTurnProvenance Provenance => new(new(Guid.NewGuid()), HostRequest.Create(RequestOrigin.LocalUi),
        new(ModelProviderIdentity.Claude, new(Guid.NewGuid()), new(1)));

    [Fact]
    public void Complete_post_is_immutable_and_contains_no_credential()
    {
        var body = Encoding.UTF8.GetBytes("{\"model\":\"exact-model\"}");
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        { ["Content-Type"] = "application/json", ["Host"] = "api.anthropic.com" };
        var transmission = new ClaudeTransmission(Provenance, Profile, new("https://api.anthropic.com/v1/messages"), body, headers);
        body[0] = 0; headers["Content-Type"] = "changed";
        transmission.Body[0].Should().Be((byte)'{');
        transmission.Headers["Content-Type"].Should().Be("application/json");
        transmission.Method.Should().Be("POST");
        transmission.Profile.Should().Be(Profile);
        transmission.Provenance.Request.Origin.Should().Be(RequestOrigin.LocalUi);
        transmission.Destination.AbsolutePath.Should().Be("/v1/messages");
        new ClaudeTurnReply(ModelTurnOutcome.Succeeded, "real").Answer.Should().Be("real");
    }

    [Theory]
    [InlineData("http://api.anthropic.com/v1/messages")]
    [InlineData("https://api.anthropic.com:444/v1/messages")]
    [InlineData("https://api.anthropic.com.evil/v1/messages")]
    [InlineData("https://secret@api.anthropic.com/v1/messages")]
    [InlineData("https://api.anthropic.com/v1/messages?retry=1")]
    [InlineData("https://api.anthropic.com/v1/messages#fragment")]
    [InlineData("https://api.anthropic.com/v1/files")]
    public void Nonexact_destination_is_not_admitted(string uri) =>
        Assert.Throws<InvalidDataException>(() => new ClaudeTransmission(Provenance, Profile, new(uri), new byte[] { 1 }, []));

    [Theory]
    [InlineData("x-api-key", "secret")]
    [InlineData("Authorization", "Bearer secret")]
    [InlineData("Proxy-Authorization", "secret")]
    [InlineData("Cookie", "secret")]
    [InlineData("Set-Cookie", "secret")]
    [InlineData("Host", "foreign")]
    [InlineData("X-Test", "new\r\nheader")]
    public void Credential_routing_and_injected_headers_never_become_disclosure(string header, string value) =>
        Assert.Throws<InvalidDataException>(() => new ClaudeTransmission(Provenance, Profile,
            new("https://api.anthropic.com/v1/messages"), new byte[] { 1 }, [KeyValuePair.Create(header, value)]));

    [Theory]
    [InlineData(0)]
    [InlineData(ClaudeTransmission.MaximumBodyBytes + 1)]
    public void Complete_body_must_fit_without_truncation(int size) =>
        Assert.Throws<InvalidDataException>(() => new ClaudeTransmission(Provenance, Profile,
            new("https://api.anthropic.com/v1/messages"), new byte[size], []));

    [Fact]
    public void Wrong_service_and_relative_destination_fail_closed()
    {
        Assert.Throws<InvalidDataException>(() => new ClaudeTransmission(Provenance, Profile with { Service = ProviderService.Copilot },
            new("https://api.anthropic.com/v1/messages"), new byte[] { 1 }, []));
        ClaudeTransmission.AllowedDestination(new Uri("relative", UriKind.Relative)).Should().BeFalse();
        ClaudeTransmission.AllowedDestination(new("https://api.anthropic.com/v1/messages/count_tokens")).Should().BeTrue();
    }

    [Theory]
    [InlineData("claude", SessionRequestReview.ProviderChoice.Claude)]
    [InlineData("CLAUDE", SessionRequestReview.ProviderChoice.Claude)]
    [InlineData("copilot", SessionRequestReview.ProviderChoice.Copilot)]
    [InlineData("", SessionRequestReview.ProviderChoice.Default)]
    public void Single_parser_adds_only_explicit_Claude_and_preserves_body(string provider, SessionRequestReview.ProviderChoice expected)
    {
        const string body = "  original\r\n\tbytes  ";
        var command = SessionCommand.Parse("session request review " + Guid.NewGuid().ToString("D") + " 1 0 " + provider + " -- " + body, "Kora")!;
        command.RequestProvider.Should().Be(expected);
        command.RequestBody.Should().Be(body);
        ((int)SessionRequestReview.ProviderChoice.Copilot).Should().Be(1);
    }
}
