using AwesomeAssertions;
using Kora.Core.Configuration;

namespace Kora.Core.UnitTests.Configuration;

public sealed class AppearanceOptionRegistryTests
{
    [Fact]
    public void Catalogue_is_bounded_typed_read_only_and_appearance_only()
    {
        AppearanceOptionRegistry.Options.Should().HaveCount(9);
        AppearanceOptionRegistry.Options.Select(item => item.Id).Should().OnlyHaveUniqueItems();
        foreach (var descriptor in AppearanceOptionRegistry.Options)
        {
            AppearanceOptionRegistry.Get(descriptor.Option).Should().BeSameAs(descriptor);
            descriptor.Scope.Should().Be("device-local");
            descriptor.Effect.Should().Be("appearance-only");
            descriptor.Availability.Should().Be("local-host");
            descriptor.ApplicationTiming.Should().Be("immediate-after-save");
            descriptor.IsCallSensitive.Should().BeFalse();
            descriptor.CanReset.Should().BeTrue();
            descriptor.Units.Should().NotBeNullOrEmpty();
            descriptor.Description.Should().NotBeNullOrEmpty();
            descriptor.AuditAction.Should().StartWith("configuration.");
            AppearanceOptionRegistry.Validate(descriptor.Option, descriptor.Default);
            if (descriptor.Minimum is { } minimum && descriptor.Maximum is { } maximum)
            {
                descriptor.Type.Should().Be(AppearanceValueType.Number);
                AppearanceOptionRegistry.Validate(descriptor.Option, new AppearanceValue.Number(minimum));
                AppearanceOptionRegistry.Validate(descriptor.Option, new AppearanceValue.Number(maximum));
                var below = () => AppearanceOptionRegistry.Validate(descriptor.Option, new AppearanceValue.Number(minimum - 1));
                var above = () => AppearanceOptionRegistry.Validate(descriptor.Option, new AppearanceValue.Number(maximum + 1));
                below.Should().Throw<ArgumentOutOfRangeException>();
                above.Should().Throw<ArgumentOutOfRangeException>();
            }
            else
            {
                descriptor.Type.Should().BeOneOf(AppearanceValueType.Theme, AppearanceValueType.Toggle);
            }
            var mismatch = () => AppearanceOptionRegistry.Validate(descriptor.Option,
                descriptor.Type == AppearanceValueType.Number ? new AppearanceValue.Toggle(true) : new AppearanceValue.Number(1));
            mismatch.Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    [Fact]
    public void Unknown_options_null_values_and_undefined_themes_are_rejected()
    {
        var unknown = () => AppearanceOptionRegistry.Get((AppearanceOption)999);
        var nullValue = () => AppearanceOptionRegistry.Validate(AppearanceOption.Theme, null!);
        var invalidTheme = () => AppearanceOptionRegistry.Validate(AppearanceOption.Theme,
            new AppearanceValue.Theme((ApplicationThemeMode)999));
        unknown.Should().Throw<ArgumentOutOfRangeException>();
        nullValue.Should().Throw<ArgumentNullException>();
        invalidTheme.Should().Throw<ArgumentOutOfRangeException>();
        var numberAsTheme = () => new AppearanceValue.Number(1).GetTheme();
        var themeAsNumber = () => new AppearanceValue.Theme(ApplicationThemeMode.System).GetNumber();
        var numberAsToggle = () => new AppearanceValue.Number(1).GetToggle();
        numberAsTheme.Should().Throw<InvalidOperationException>();
        themeAsNumber.Should().Throw<InvalidOperationException>();
        numberAsToggle.Should().Throw<InvalidOperationException>();
        new AppearanceValue.Theme(ApplicationThemeMode.Dark).GetTheme().Should().Be(ApplicationThemeMode.Dark);
        new AppearanceValue.Number(2).GetNumber().Should().Be(2);
        new AppearanceValue.Toggle(false).GetToggle().Should().BeFalse();
    }
}
