using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class ModelAnswerUpdateTests
{
    [Theory]
    [InlineData(" ")]
    [InlineData("answer\r\n\t\U0001F642")]
    public void ProvisionalAnswerAcceptsDecodedTextWithoutClaimingFinalCompleteness(string text)
    {
        var update = new ModelAnswerUpdate(Provenance(), text);
        update.Invoking(value => value.Validate()).Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("text\0")]
    public void ProvisionalAnswerRejectsInvalidDecodedText(string? text)
    {
        new ModelAnswerUpdate(Provenance(), text!).Invoking(value => value.Validate())
            .Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ProvisionalAnswerRejectsUnpairedSurrogateWithoutTestDataSerialization()
    {
        new ModelAnswerUpdate(Provenance(), new string('\uD800', 1)).Invoking(value => value.Validate())
            .Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ProvisionalAnswerEnforcesExactInclusiveUtf8Limit(int extra)
    {
        var update = new ModelAnswerUpdate(Provenance(), new string('x', ModelToolProtocol.MaximumOutputUtf8Bytes + extra));
        var validate = () => update.Validate();
        if (extra == 0) { validate.Should().NotThrow(); }
        else { validate.Should().Throw<InvalidDataException>(); }
    }

    private static ModelTurnProvenance Provenance() =>
        new(new(Guid.NewGuid()), HostRequest.Create(RequestOrigin.LocalUi), ModelProviderSelection.OllamaCandidate);
}
