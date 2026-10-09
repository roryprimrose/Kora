using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Configuration;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task Native_session_preferences_save_refresh_reset_only_future_clock_settings()
    {
        var fixture = new Fixture(enableSessionRetention: true, enableDiagnosticRetention: true);
        await using var admission = fixture.DiagnosticAdmission;
        fixture.ViewModel.SessionRetentionChoices.Should().HaveCount(365);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.CanChangeSessionRetentionNative.Should().BeFalse();
        await fixture.ViewModel.SaveSessionRetentionCommand.ExecuteAsync();
        fixture.ViewModel.SessionRetentionStatus.Should().Contain("owning unlocked native");
        fixture.ViewModel.BindSessionRetentionNativeLifetime(static () => true);
        fixture.ViewModel.CanChangeSessionRetentionNative.Should().BeFalse();
        fixture.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => true);
        fixture.ViewModel.CanChangeSessionRetentionNative.Should().BeTrue();
        await fixture.ViewModel.RefreshSessionRetentionCommand.ExecuteAsync();
        fixture.ViewModel.SelectedSessionArchiveDays.Should().Be(1);
        fixture.ViewModel.SelectedSessionDeleteDays.Should().Be(30);
        fixture.ViewModel.SelectedSessionArchiveDays = 2;
        fixture.ViewModel.SelectedSessionDeleteDays = 60;
        await fixture.ViewModel.SaveSessionRetentionCommand.ExecuteAsync();
        fixture.SessionRetentionPreferences.Value.Should().Be(new SessionRetentionSettings(2, 60));
        fixture.ViewModel.SessionRetentionStatus.Should().Contain("existing due dates are unchanged");
        await fixture.ViewModel.ResetSessionRetentionCommand.ExecuteAsync();
        fixture.SessionRetentionPreferences.Value.Should().BeNull();
        fixture.ViewModel.SelectedSessionArchiveDays.Should().Be(1);
        fixture.ViewModel.SelectedSessionDeleteDays.Should().Be(30);
        fixture.DiagnosticPreferences.Value.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("state")]
    [InlineData("range")]
    [InlineData("revoked")]
    public async Task Invalid_unconfirmed_or_revoked_native_changes_are_explicit_not_success(string failure)
    {
        var fixture = new Fixture(enableSessionRetention: true, enableDiagnosticRetention: true);
        await using var admission = fixture.DiagnosticAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => true);
        fixture.ViewModel.BindSessionRetentionNativeLifetime(static () => true);
        fixture.SessionRetentionPreferences.Failure = failure switch
        {
            "invalid" => new InvalidDataException("invalid preference"),
            "io" => new IOException("write failed"),
            "access" => new UnauthorizedAccessException("access failed"),
            "state" => new InvalidOperationException("unconfirmed"),
            _ => null,
        };
        if (failure is "range") { fixture.ViewModel.SelectedSessionDeleteDays = 1; }
        if (failure is "revoked")
        {
            fixture.Audit.BeforeWrite = audit =>
            {
                if (string.Equals(audit.ActionId, "configuration.session-retention", StringComparison.Ordinal)
                    && audit.Outcome == SecurityAuditOutcome.Requested)
                {
                    fixture.ViewModel.BindSessionRetentionNativeLifetime(static () => false);
                }
            };
        }
        await fixture.ViewModel.SaveSessionRetentionCommand.ExecuteAsync();
        fixture.SessionRetentionPreferences.Value.Should().BeNull();
        fixture.ViewModel.SessionRetentionStatus.Should().Contain(failure is "revoked" ? "denied" : "unavailable");
        fixture.ViewModel.Dispose();
    }

    private sealed class FakeSessionRetentionPreferences : ISessionRetentionPreferences
    {
        internal SessionRetentionSettings? Value { get; set; }
        internal Exception? Failure { get; set; }
        public SessionRetentionSettings? Load() => Failure is { } failure ? throw failure : Value;
        public SessionRetentionSettings? ReadBack() => Load();
        public void BeginWrite() { }
        public void ConfirmWrite() { }
        public void Save(SessionRetentionSettings settings) => Value = settings;
        public void Reset() => Value = null;
    }
}
