using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.ViewModels;
using Kora.Core.Auditing;
using Kora.Core.Configuration;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(75)]
    [InlineData(100)]
    [InlineData(200)]
    public async Task ReaderTypographyUsesExactBoundsAndOneCanonicalNativeCommandResetService(int percent)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.ReaderTextScaleMinimum.Should().Be(75);
        fixture.ViewModel.ReaderTextScaleMaximum.Should().Be(200);
        fixture.ViewModel.ReaderTextScalePercent.Should().Be(100);
        fixture.ViewModel.ReaderTextScalePercent = percent;
        await fixture.ViewModel.AppearanceUpdate;
        fixture.ViewModel.ReaderTextScalePercent.Should().Be(percent);
        fixture.ViewModel.EnsureReaderTypographyAvailable();
        fixture.ViewModel.SelectedAppearanceOption = AppearanceOptionRegistry.Get(AppearanceOption.ReaderTextScale);
        await fixture.ViewModel.ResetAppearanceOptionCommand.ExecuteAsync();
        fixture.ViewModel.ReaderTextScalePercent.Should().Be(100);
        await fixture.ViewModel.ExecuteAppearanceCommandAsync(
            AppearanceCommand.Parse("set appearance.reader-text-scale to 200", "Kora")!, SecurityAuditInitiator.TypedCommand);
        fixture.ViewModel.ReaderTextScalePercent.Should().Be(200);
        await fixture.ViewModel.ExecuteAppearanceCommandAsync(
            AppearanceCommand.Parse("status appearance.reader-text-scale", "Kora")!, SecurityAuditInitiator.TypedCommand);
        fixture.ViewModel.ResponseBody.Should().Contain("appearance.reader-text-scale = 200 percent").And.Contain("Saved preference");
    }

    [Theory]
    [InlineData("disk")]
    [InlineData("access")]
    [InlineData("data")]
    [InlineData("origin")]
    [InlineData("range")]
    public async Task ReaderTypographyFailureIsExplicitAndNeverReportedAsSaved(string failure)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.AppearancePreferences.SaveException = failure switch
        {
            "disk" => new IOException("reader disk full"),
            "access" => new UnauthorizedAccessException("reader access denied"),
            "data" => new InvalidDataException("reader corrupt"),
            "origin" => new InvalidOperationException("reader admission changed"),
            _ => null,
        };
        fixture.ViewModel.ReaderTextScalePercent = string.Equals(failure, "range", StringComparison.Ordinal) ? 201 : 75;
        await fixture.ViewModel.AppearanceUpdate;
        fixture.ViewModel.ReaderTextScalePercent.Should().Be(100);
        fixture.ViewModel.ResponseTitle.Should().Be("Native reader text scale is unavailable.");
        fixture.ViewModel.AppearanceSettingStatus.Should().Contain("not confirmed");
        if (!string.Equals(failure, "range", StringComparison.Ordinal))
        {
            var open = fixture.ViewModel.EnsureReaderTypographyAvailable;
            open.Should().Throw<InvalidOperationException>().WithMessage("*text scale is unavailable*");
        }
    }

    [Fact]
    public async Task LockedHostCannotChangeReaderScaleOrUseItsPreferenceAsContentAuthority()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Session.IsUnlocked = false;
        fixture.ViewModel.ReaderTextScalePercent = 75;
        fixture.ViewModel.ReaderTextScalePercent.Should().Be(100);
        fixture.AppearancePreferences.ReaderTextScalePercent.Should().BeNull();
        fixture.ViewModel.ResponseTitle.Should().Be("Appearance settings are unavailable.");
    }
}
