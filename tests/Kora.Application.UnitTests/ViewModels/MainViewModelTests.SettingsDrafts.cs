using System.Reflection;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.ViewModels;
using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Numeric_saved_baselines_distinguish_confirmed_and_unavailable_state(bool configured)
    {
        var fixture = new Fixture(enablePlaybackVolume: configured, enableWindowsSpeechRate: configured,
            enableQueueConfiguration: configured, enableSessionRetention: configured,
            enableDiagnosticRetention: configured, enableAuditRetention: configured);
        await using var audio = fixture.OutputAdmission;
        await using var diagnostic = fixture.DiagnosticAdmission;
        await using var audit = fixture.AuditAdmission;
        await fixture.ViewModel.InitializeAsync();
        var viewModel = fixture.ViewModel;
        if (configured)
        {
            fixture.SessionRetentionConfiguration!.Observe();
            NumericSavedBaselines(viewModel).Should().NotContainNulls();
            viewModel.SavedWindowsSpeechRate.Should().Be(0);
            viewModel.SelectedSessionDeleteDays++;
            viewModel.IsSessionRetentionDraftChanged.Should().BeTrue();
            fixture.VolumeConfiguration!.HoldUnavailable();
            fixture.RateConfiguration!.HoldUnavailable();
            fixture.QueueConfiguration!.HoldUnavailable();
            fixture.DiagnosticConfiguration!.HoldUnavailable();
            fixture.AuditConfiguration!.HoldUnavailable();
            fixture.SessionRetentionPreferences.Failure = new IOException("Unavailable preference.");
            var observe = () => fixture.SessionRetentionConfiguration!.Observe();
            observe.Should().Throw<IOException>();
        }
        NumericSavedBaselines(viewModel).Should().OnlyContain(value => value == null);
        viewModel.IsSessionRetentionDraftChanged.Should().BeFalse();
        viewModel.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manual_control_status_and_absent_field_review_preserve_availability(bool enabled)
    {
        var fixture = new Fixture(enableManualCallControl: enabled);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        if (enabled) { fixture.ViewModel.ManualCallConfigurationStatus.Should().Be(fixture.ViewModel.CallStateStatus); }
        else { fixture.ViewModel.ManualCallConfigurationStatus.Should().Contain("unavailable").And.Contain("protections remain active"); }
        var serialize = typeof(MainViewModel).GetMethod("SerializeProviderFieldPreview", BindingFlags.NonPublic | BindingFlags.Instance)!;
        serialize.Invoke(fixture.ViewModel, null).Should().BeNull();
        fixture.ViewModel.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Structured_speech_inspection_preserves_invalid_and_saved_metadata(bool saved)
    {
        var state = new SpeechConfigurationState(null, null, [], [], 1, saved, null)
        {
            SummaryLimits = saved ? SpokenSummaryLimits.Default : null,
            AreSummaryLimitsSaved = saved,
            SummaryLimitsRecovery = saved ? null : "Refresh the invalid summary limits.",
        };
        var describe = typeof(MainViewModel).GetMethod("DescribeSpeechConfiguration", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = describe.Invoke(null, [state]);
        result.Should().BeOfType<string>().Which.Should().Contain("speech.provider = invalid")
            .And.Contain("speech.voice = default").And.Contain("effective voice = unavailable");
    }

    private static int?[] NumericSavedBaselines(MainViewModel viewModel) =>
    [
        viewModel.SavedPlaybackVolume, viewModel.SavedWindowsSpeechRate,
        viewModel.SavedQueuePending, viewModel.SavedQueueSlots,
        viewModel.SavedQueueLifetimeMinutes, viewModel.SavedQueueActiveBudgetMinutes,
        viewModel.SavedSessionArchiveDays, viewModel.SavedSessionDeleteDays,
        viewModel.SavedDiagnosticRetentionDays, viewModel.SavedAuditRetentionDays,
    ];
}
