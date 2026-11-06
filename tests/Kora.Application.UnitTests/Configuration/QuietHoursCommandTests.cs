using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;

namespace Kora.Application.UnitTests.Configuration;

public sealed class QuietHoursCommandTests
{
    [Theory]
    [InlineData("get notices.quiet-hours", AppearanceCommandOperation.Get)]
    [InlineData("set notices.quiet-hours to Off", AppearanceCommandOperation.Set)]
    [InlineData("set notices.quiet-hours to weekly Mon,Fri 22:00 06:00 UTC", AppearanceCommandOperation.Set)]
    [InlineData("reset notices.quiet-hours", AppearanceCommandOperation.Reset)]
    [InlineData("Nova, get notices.quiet-hours", AppearanceCommandOperation.Get)]
    public void ExactCommandsAndCurrentNameShareTypedValidation(string text, AppearanceCommandOperation operation)
    {
        var command = QuietHoursCommand.Parse(text, "Nova")!;
        command.Operation.Should().Be(operation);
        command.Error.Should().BeNull();
        command.ReviewDecision.Should().BeNull();
        if (operation == AppearanceCommandOperation.Set) { command.Value.Should().NotBeNull(); }
    }

    [Theory]
    [InlineData("confirm notices.quiet-hours", true)]
    [InlineData("cancel notices.quiet-hours", false)]
    public void ConfirmationIsSeparateAndExact(string text, bool decision)
    {
        QuietHoursCommand.Parse(text, "Kora")!.ReviewDecision.Should().Be(decision);
        QuietHoursCommand.FixedPhrases.Should().Contain(text);
        QuietHoursCommand.Syntax.Should().Contain(text);
    }

    [Theory]
    [InlineData("set notices.quiet-hours to Off and enable listening")]
    [InlineData("get notices.quiet-hours now")]
    [InlineData("set notices.quiet-hours to weekdays nine to five")]
    [InlineData("set notices.quiet-hours to weekly Mon 09:00 10:00 missing-zone")]
    [InlineData("confirm notices.quiet-hours and approve")]
    [InlineData("get notices.quiet-hours\n")]
    public void InvalidTargetedInputClarifiesWithoutSettingAnything(string text)
    {
        var command = QuietHoursCommand.Parse(text, "Kora")!;
        command.Operation.Should().Be(AppearanceCommandOperation.Clarify);
        command.Value.Should().BeNull();
        command.Error.Should().Be(QuietHoursCommand.Syntax);
    }

    [Theory]
    [InlineData("get notices.quiet-hours-other")]
    [InlineData("Kora, confirm notices.quiet-hours")]
    [InlineData("tell me the time")]
    public void OtherNamesAndOtherCommandsAreNotScheduleAuthority(string text)
    {
        QuietHoursCommand.Parse(text, "Nova").Should().BeNull();
    }

    [Fact]
    public void OversizeAndControlCharactersCannotReachASetter()
    {
        QuietHoursCommand.Parse("set notices.quiet-hours to " + new string('x', 40_000), "Kora")!.Operation
            .Should().Be(AppearanceCommandOperation.Clarify);
        QuietHoursCommand.Parse("get notices.quiet-hours\t", "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
        QuietHoursCommand.Parse("set notices.quiet-hours to Off", "Kora")!.Value.Should().Be(QuietHoursSchedule.Off);
    }
}
