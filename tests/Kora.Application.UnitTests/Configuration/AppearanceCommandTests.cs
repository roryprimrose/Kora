using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;

namespace Kora.Application.UnitTests.Configuration;

public sealed class AppearanceCommandTests
{
    [Theory]
    [InlineData("Kora, list appearance settings", "Kora")]
    [InlineData("Nova list appearance settings", "Nova")]
    [InlineData(" Nova, LIST APPEARANCE SETTINGS ", "Nova")]
    [InlineData("list appearance settings", "Nova")]
    public void Exact_discovery_respects_current_assistant_name(string transcript, string name) =>
        AppearanceCommand.Parse(transcript, name)!.Operation.Should().Be(AppearanceCommandOperation.List);

    [Theory]
    [InlineData("Kora, list appearance settings", "Nova")]
    [InlineData("Novation, list appearance settings", "Nova")]
    [InlineData("Nova", "Nova")]
    [InlineData("what can you do", "Kora")]
    public void Unrelated_input_and_obsolete_names_do_not_claim_exact_routing(string transcript, string name) =>
        AppearanceCommand.Parse(transcript, name).Should().BeNull();

    [Fact]
    public void Every_option_has_exact_get_set_reset_and_one_authoritative_validation()
    {
        foreach (var descriptor in AppearanceOptionRegistry.Options)
        {
            AppearanceCommand.Parse($"get {descriptor.Id}", "Kora")!.Descriptor.Should().BeSameAs(descriptor);
            AppearanceCommand.Parse($"reset {descriptor.Id}", "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Reset);
            var setter = AppearanceCommand.Parse($"set {descriptor.Id} to {AppearanceCommand.Format(descriptor.Default)}", "Kora");
            setter!.Operation.Should().Be(AppearanceCommandOperation.Set);
            setter.Value.Should().Be(descriptor.Default);
            AppearanceCommand.Parse($"get {descriptor.SpokenName}", "Kora")!.Descriptor.Should().BeSameAs(descriptor);
            AppearanceCommand.Parse($"reset {descriptor.SpokenName}", "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Reset);
            AppearanceCommand.Parse($"set {descriptor.SpokenName} to {AppearanceCommand.Format(descriptor.Default)}", "Kora")!
                .Value.Should().Be(descriptor.Default);
        }
        AppearanceCommand.Parse("set appearance.theme to light", "Kora")!.Value.Should().Be(new AppearanceValue.Theme(ApplicationThemeMode.Light));
        AppearanceCommand.Parse("set appearance.theme to dark", "Kora")!.Value.Should().Be(new AppearanceValue.Theme(ApplicationThemeMode.Dark));
        AppearanceCommand.Parse("set appearance.speech-scaling to false", "Kora")!.Value.Should().Be(new AppearanceValue.Toggle(false));
    }

    [Theory]
    [InlineData("set appearance.theme to 2")]
    [InlineData("set appearance.theme to unknown")]
    [InlineData("set appearance.theme to dark and install software")]
    [InlineData("reset appearance")]
    [InlineData("get appearance.audio")]
    [InlineData("set appearance.presence-size to -240")]
    [InlineData("set appearance.presence-timeout to 0")]
    [InlineData("set appearance.presence-timeout to 1.5")]
    [InlineData("set appearance.presence-timeout to 2147483648")]
    [InlineData("set appearance.presence-timeout to ten")]
    [InlineData("set appearance.presence-timeout to 10 seconds")]
    [InlineData("set appearance.speech-scaling to yes")]
    [InlineData("list appearance")]
    [InlineData("list appearance.theme")]
    [InlineData("set appearance")]
    [InlineData("set appearance.theme")]
    [InlineData("set appearance.theme to")]
    [InlineData("get appearance.theme extra")]
    public void Ambiguous_unknown_out_of_range_and_injected_commands_clarify_without_clamping(string transcript)
    {
        var command = AppearanceCommand.Parse(transcript, "Kora");
        command!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
        command.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Oversized_input_and_missing_format_values_are_rejected()
    {
        AppearanceCommand.Parse("set appearance.theme to " + new string('x', 300), "Kora")!
            .Error.Should().Contain("too long");
        var format = () => AppearanceCommand.Format(null!);
        format.Should().Throw<ArgumentOutOfRangeException>();
    }
}
