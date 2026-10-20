using AwesomeAssertions;
using Kora.Core.Utilities;
using Kora.Tools.Utilities;

namespace Kora.Tools.UnitTests.Utilities;

public sealed class UuidActionsTests
{
    private const string Canonical = "12345678-9abc-4def-8123-456789abcdef";

    [Fact]
    public void GeneratesOnceWithExplicitStandaloneDisclaimer()
    {
        var generator = new Generator();
        var result = new UuidGenerate(generator).Execute(TestContext.Current.CancellationToken);
        generator.Calls.Should().Be(1);
        result.Should().Contain(Canonical).And.Contain(UuidHelp.Disclaimer);
        new UuidHelp().Execute().Should().Contain(UuidHelp.Syntax).And.Contain(UuidHelp.Disclaimer);
        var host = new RandomUtilityUuidGenerator().Generate();
        var parsed = UtilityUuid.Parse(host.ToString("D"));
        parsed.VersionNibble.Should().Be(4);
        parsed.IsRfcVariant.Should().BeTrue();
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("12345678-9abc-3def-8123-456789abcdef")]
    [InlineData("12345678-9abc-4def-c123-456789abcdef")]
    public void RejectsInvalidGeneratorOutputWithoutRetry(string text)
    {
        var generator = new Generator { Value = Guid.Parse(text) };
        var act = () => new UuidGenerate(generator).Execute(TestContext.Current.CancellationToken);
        act.Should().Throw<InvalidDataException>();
        generator.Calls.Should().Be(1);
    }

    [Fact]
    public void PropagatesProviderFailureAndCancellationWithoutRetry()
    {
        var generator = new Generator { Failure = new InvalidOperationException("provider failed") };
        var act = () => new UuidGenerate(generator).Execute(TestContext.Current.CancellationToken);
        act.Should().Throw<InvalidOperationException>();
        generator.Calls.Should().Be(1);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        act = () => new UuidGenerate(generator).Execute(cancelled.Token);
        act.Should().Throw<OperationCanceledException>();
        generator.Calls.Should().Be(1);
        using var during = new CancellationTokenSource();
        generator.Failure = null;
        generator.Callback = during.Cancel;
        act = () => new UuidGenerate(generator).Execute(during.Token);
        act.Should().Throw<OperationCanceledException>();
        generator.Calls.Should().Be(2);
    }

    [Theory]
    [InlineData("12345678-9abc-1def-8123-456789abcdef", "1", "RFC variant")]
    [InlineData("12345678-9abc-fdef-f123-456789abcdef", "15", "unknown variant")]
    public void InspectsOnlyFormatFacts(string text, string version, string variant)
    {
        new UuidInspect().Execute(text).Should().Contain("version nibble " + version)
            .And.Contain(variant).And.Contain(UuidHelp.Disclaimer);
        var invalid = () => new UuidInspect().Execute("");
        invalid.Should().Throw<InvalidDataException>();
    }

    private sealed class Generator : IUtilityUuidGenerator
    {
        public Guid Value { get; set; } = Guid.Parse(Canonical);
        public Exception? Failure { get; set; }
        public Action? Callback { get; set; }
        public int Calls { get; private set; }
        public Guid Generate()
        {
            Calls++;
            Callback?.Invoke();
            if (Failure is not null) { throw Failure; }
            return Value;
        }
    }
}
