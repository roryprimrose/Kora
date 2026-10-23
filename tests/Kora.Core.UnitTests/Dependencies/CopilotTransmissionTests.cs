using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class CopilotTransmissionTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(131072, true)]
    [InlineData(131073, false)]
    public void Final_transmission_body_has_an_exact_complete_byte_limit(int bytes, bool valid)
    {
        var body = new byte[bytes];
        var create = () => Create(body);
        if (valid) { create().Body.Length.Should().Be(bytes); }
        else { create.Should().Throw<InvalidDataException>(); }
    }

    [Fact]
    public void Body_is_a_snapshot_not_caller_owned_mutable_memory()
    {
        var body = new byte[] { 1, 2, 3 };
        var transmission = Create(body);
        body[0] = 99;
        transmission.Body.Should().Equal(1, 2, 3);
    }

    [Theory]
    [InlineData("http://api.githubcopilot.com/chat/completions")]
    [InlineData("https://user:password@api.githubcopilot.com/chat/completions")]
    [InlineData("https://api.githubcopilot.com/chat/completions#hidden")]
    public void Non_https_or_hidden_destination_components_are_not_reviewable_transmissions(string destination)
    {
        var create = () => Create([1], destination);
        create.Should().Throw<InvalidDataException>();
    }

    private static CopilotTransmission Create(byte[] body, string destination = "https://api.githubcopilot.com/chat/completions") =>
        new(new(new(Guid.NewGuid()), HostRequest.Create(RequestOrigin.LocalUi), ModelProviderSelection.CopilotCandidate),
            new(ProviderService.Copilot, new("account"), new(1), new("model"), new("default"), new("default")), new(destination), body);
}
