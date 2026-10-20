using AwesomeAssertions;
using Kora.Core.Numbers;
using Kora.Tools.Numbers;

namespace Kora.Tools.UnitTests.Numbers;

public sealed class IntegerBaseConvertTests
{
    [Fact]
    public void HostActionDelegatesToTheAuthoritativePolicy()
    {
        var action = new IntegerBaseConvert();
        var request = new IntegerConversionRequest("-8000000000000000", IntegerBase.Hex, IntegerBase.BaseTen);
        action.Execute(request).Should().Be(IntegerBasePolicy.Convert(request));
        action.Execute(request with { Literal = "-0" }).Outcome.Should().Be(IntegerConversionOutcome.InvalidInteger);
    }
}
