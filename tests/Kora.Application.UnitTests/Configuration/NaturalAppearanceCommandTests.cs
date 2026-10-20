using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Configuration;

namespace Kora.Application.UnitTests.Configuration;

public sealed class NaturalAppearanceCommandTests
{
    [Theory]
    [InlineData("use dark mode", ApplicationThemeMode.Dark)]
    [InlineData("use light mode", ApplicationThemeMode.Light)]
    [InlineData("use system theme", ApplicationThemeMode.System)]
    [InlineData(" Nova, USE DARK MODE ", ApplicationThemeMode.Dark)]
    [InlineData("Nova use light mode", ApplicationThemeMode.Light)]
    public void ParseSelectsOnlyExactAppTheme(string text, ApplicationThemeMode mode)
    {
        var command = NaturalAppearanceCommand.Parse(text, "Nova")!;
        command.Operation.Should().Be(AppearanceCommandOperation.Set);
        command.Descriptor.Should().BeSameAs(AppearanceOptionRegistry.Get(AppearanceOption.Theme));
        command.Value.Should().Be(new AppearanceValue.Theme(mode));
        command.ReaderTextScaleAdjustment.Should().BeNull();
    }

    [Theory]
    [InlineData("get reader text scale", AppearanceCommandOperation.Get)]
    [InlineData("status reader text scale", AppearanceCommandOperation.Get)]
    [InlineData("reset reader text scale", AppearanceCommandOperation.Reset)]
    [InlineData("Nova, RESET READER TEXT SCALE", AppearanceCommandOperation.Reset)]
    public void ParseQueriesOrResetsOnlyTheReaderOption(string text, AppearanceCommandOperation operation)
    {
        var command = NaturalAppearanceCommand.Parse(text, "Nova")!;
        command.Operation.Should().Be(operation);
        command.Descriptor.Should().BeSameAs(AppearanceOptionRegistry.Get(AppearanceOption.ReaderTextScale));
        command.Value.Should().BeNull();
    }

    [Theory]
    [InlineData(75)]
    [InlineData(100)]
    [InlineData(200)]
    public void ParseValidatesAbsoluteBoundsThroughRegistry(int value)
    {
        var command = NaturalAppearanceCommand.Parse($"set reader text scale to {value} percent", "Kora")!;
        command.Operation.Should().Be(AppearanceCommandOperation.Set);
        command.Value.Should().Be(new AppearanceValue.Number(value));
        command.ReaderTextScaleAdjustment.Should().BeNull();
    }

    [Theory]
    [InlineData("increase", 1, 1)]
    [InlineData("increase", 125, 125)]
    [InlineData("increase", int.MaxValue, int.MaxValue)]
    [InlineData("decrease", 25, -25)]
    [InlineData("decrease", int.MaxValue, -int.MaxValue)]
    public void ParseRetainsPercentagePointsForFreshExecutionResolution(string operation, int value, int delta)
    {
        var command = NaturalAppearanceCommand.Parse($"Nova, {operation} reader text scale by {value} percentage points", "Nova", true)!;
        command.Operation.Should().Be(AppearanceCommandOperation.Set);
        command.Descriptor!.Option.Should().Be(AppearanceOption.ReaderTextScale);
        command.ReaderTextScaleAdjustment.Should().Be(delta);
        command.Value.Should().BeNull();
    }

    [Theory]
    [InlineData("use dark mode")]
    [InlineData("get reader text scale")]
    [InlineData("set reader text scale to 125 percent")]
    public void ParseRequiresCurrentNameForVoiceButNotTypedInput(string text)
    {
        NaturalAppearanceCommand.Parse(text, "Nova", true)!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
        NaturalAppearanceCommand.Parse("Nova, " + text, "Nova", true)!.Operation.Should().NotBe(AppearanceCommandOperation.Clarify);
        NaturalAppearanceCommand.Parse(text, "Nova")!.Operation.Should().NotBe(AppearanceCommandOperation.Clarify);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Nova")]
    [InlineData("Kora, use dark mode")]
    [InlineData("Novation, use dark mode")]
    [InlineData("please use dark mode")]
    [InlineData("use dark mode and open settings")]
    [InlineData("use blue mode")]
    [InlineData("use system mode")]
    [InlineData("make it bigger")]
    [InlineData("increase selected window text scale by 10 percentage points")]
    [InlineData("summarize: set reader text scale to 150 percent")]
    [InlineData("set reader text scales to 150 percent")]
    [InlineData("get reader text scales")]
    [InlineData("use\ndark mode")]
    public void ParseDoesNotClaimUnrelatedAmbiguousOrEmbeddedCommands(string text) =>
        NaturalAppearanceCommand.Parse(text, "Nova").Should().BeNull();

    [Theory]
    [InlineData("get reader text scale extra")]
    [InlineData("status reader text scale and reset reader text scale")]
    [InlineData("reset reader text scale to 100")]
    [InlineData("set reader text scale")]
    [InlineData("set reader text scale to")]
    [InlineData("set reader text scale to percent")]
    [InlineData("set reader text scale to  percent")]
    [InlineData("set reader text scale to 74 percent")]
    [InlineData("set reader text scale to 201 percent")]
    [InlineData("set reader text scale to 0 percent")]
    [InlineData("set reader text scale to -75 percent")]
    [InlineData("set reader text scale to +75 percent")]
    [InlineData("set reader text scale to 1.5 percent")]
    [InlineData("set reader text scale to 1,000 percent")]
    [InlineData("set reader text scale to 2147483648 percent")]
    [InlineData("set reader text scale to one hundred percent")]
    [InlineData("set reader text scale to 150%")]
    [InlineData("set reader text scale to 150 percent and use dark mode")]
    [InlineData("increase reader text scale")]
    [InlineData("increase reader text scale by percentage points")]
    [InlineData("increase reader text scale by 0 percentage points")]
    [InlineData("increase reader text scale by -10 percentage points")]
    [InlineData("increase reader text scale by +10 percentage points")]
    [InlineData("increase reader text scale by 10 percent")]
    [InlineData("increase reader text scale by 2147483648 percentage points")]
    [InlineData("decrease reader text scale by 2.5 percentage points")]
    [InlineData("decrease reader text scale by 10 percentage points extra")]
    [InlineData("set reader text scale to 150\npercent")]
    public void ParseClarifiesMalformedOrOutOfRangeReaderRequestsWithoutClamping(string text)
    {
        var command = NaturalAppearanceCommand.Parse(text, "Nova")!;
        command.Operation.Should().Be(AppearanceCommandOperation.Clarify);
        command.Error.Should().Be(NaturalAppearanceCommand.Syntax);
        command.Value.Should().BeNull();
        command.ReaderTextScaleAdjustment.Should().BeNull();
    }

    [Fact]
    public void ParseRejectsOversizedRecognizedInput() =>
        NaturalAppearanceCommand.Parse("set reader text scale to " + new string('1', 300) + " percent", "Kora")!
            .Operation.Should().Be(AppearanceCommandOperation.Clarify);
}
