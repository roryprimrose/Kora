using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class SessionRetentionConfigurationServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public SessionRetentionConfigurationServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task Preference_activation_follows_required_audit_and_terminal_receipt_and_reset_is_future_only()
    {
        await using var fixture = new Fixture();
        fixture.Service.Available.Should().BeFalse();
        fixture.Service.Observe();
        fixture.Policy.Settings.Should().Be(SessionRetentionSettings.Default);
        fixture.Service.Settings.Should().Be(SessionRetentionSettings.Default);
        var old = fixture.Policy.Settings;
        fixture.Audit.BeforeWrite = _ => fixture.Policy.Settings.Should().Be(old);
        (await fixture.Save(new(2, 60))).Should().BeTrue();
        fixture.Policy.Settings.Should().Be(new SessionRetentionSettings(2, 60));
        fixture.Preferences.Pending.Should().BeFalse();
        fixture.Store.Tasks.Where(task => task.IsTerminal).Should().OnlyContain(task => task.State == HostTaskState.Succeeded);
        fixture.Audit.BeforeWrite = null;
        fixture.Service.Observe();
        (await fixture.Save(null)).Should().BeTrue();
        fixture.Policy.Settings.Should().Be(SessionRetentionSettings.Default);
        fixture.Preferences.Value.Should().BeNull();
        fixture.Audit.Events.Chunk(2).Should().OnlyContain(pair =>
            pair[0].Outcome == SecurityAuditOutcome.Requested && pair[1].Outcome == SecurityAuditOutcome.Succeeded
            && pair[0].ActionId == "configuration.session-retention");
    }

    [Theory]
    [InlineData("read")]
    [InlineData("begin")]
    [InlineData("write")]
    [InlineData("access")]
    [InlineData("readback")]
    [InlineData("requested")]
    [InlineData("terminal")]
    [InlineData("receipt")]
    [InlineData("confirm")]
    [InlineData("confirmed-change")]
    [InlineData("revoked")]
    [InlineData("reentrant")]
    [InlineData("before-confirm-change")]
    [InlineData("confirmed-host")]
    [InlineData("before-confirm-host")]
    [InlineData("before-confirm-reset")]
    [InlineData("confirmed-reset")]
    [InlineData("before-confirm-missing")]
    public async Task Failed_persistence_audit_receipt_and_confirmation_hold_policy_without_fallback(string stage)
    {
        await using var fixture = new Fixture();
        fixture.Service.Observe();
        if (stage is "read") { fixture.Preferences.ReadFailure = new InvalidDataException(); }
        if (stage is "begin") { fixture.Preferences.BeginFailure = new IOException(); }
        if (stage is "write") { fixture.Preferences.WriteFailure = new IOException(); }
        if (stage is "access") { fixture.Preferences.WriteFailure = new UnauthorizedAccessException(); }
        if (stage is "readback") { fixture.Preferences.AfterSave = () => fixture.Preferences.Value = new(3, 60); }
        if (stage is "receipt") { fixture.Store.FailTerminal = true; }
        if (stage is "confirm") { fixture.Preferences.ConfirmFailure = new IOException(); }
        if (stage is "confirmed-change") { fixture.Preferences.AfterConfirm = () => fixture.Preferences.Value = new(3, 60); }
        if (stage is "revoked") { fixture.Preferences.AfterSave = () => fixture.Eligible = false; }
        if (stage is "reentrant") { fixture.Preferences.AfterSave = fixture.Service.Observe; }
        if (stage is "before-confirm-change" or "before-confirm-reset")
        {
            fixture.Preferences.AfterSave = () =>
                fixture.Store.BeforeCommit = task => { if (task.IsTerminal) { fixture.Preferences.Value = new(3, 60); } };
        }
        if (stage is "confirmed-host") { fixture.Preferences.AfterConfirm = () => fixture.Eligible = false; }
        if (stage is "before-confirm-host")
        {
            fixture.Preferences.AfterSave = () =>
                fixture.Store.BeforeCommit = task => { if (task.IsTerminal) { fixture.Eligible = false; } };
        }
        if (stage is "before-confirm-missing")
        {
            fixture.Preferences.AfterSave = () =>
                fixture.Store.BeforeCommit = task => { if (task.IsTerminal) { fixture.Preferences.Value = null; } };
        }
        if (stage is "confirmed-reset") { fixture.Preferences.AfterConfirm = () => fixture.Preferences.Value = new(3, 60); }
        fixture.Audit.BeforeWrite = item =>
        {
            if (stage is "requested" && item.Outcome == SecurityAuditOutcome.Requested
                || stage is "terminal" && item.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException(); }
        };
        var action = () => fixture.Save(stage is "before-confirm-reset" or "confirmed-reset" ? null : new(2, 60));
        await action.Should().ThrowAsync<Exception>();
        fixture.Service.Available.Should().BeFalse();
        fixture.Policy.Invoking(value => _ = value.Settings).Should().Throw<InvalidDataException>();
        if (stage is "readback" or "write" or "access")
        {
            fixture.Audit.Events.Should().Contain(item => item.Outcome == SecurityAuditOutcome.Failed);
        }
    }

    [Fact]
    public async Task Invalid_startup_and_unconfirmed_preference_never_activate_defaults()
    {
        await using var fixture = new Fixture();
        fixture.Preferences.ReadFailure = new InvalidDataException();
        fixture.Service.Invoking(value => value.Observe()).Should().Throw<InvalidDataException>();
        fixture.Service.Available.Should().BeFalse();
        fixture.Policy.Invoking(value => _ = value.Settings).Should().Throw<InvalidDataException>();
        fixture.Preferences.ReadFailure = null;
        fixture.Preferences.BeginWrite();
        fixture.Service.Invoking(value => value.Observe()).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task Cancelled_or_unadmitted_input_cannot_write_and_policy_requires_explicit_refresh()
    {
        await using var fixture = new Fixture();
        fixture.Service.Observe();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var action = () => fixture.Save(new(2, 60), cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
        fixture.Preferences.Writes.Should().Be(0);
        fixture.Service.Observe();
        fixture.Eligible = false;
        var denied = () => fixture.Save(new(2, 60));
        await denied.Should().ThrowAsync<InvalidOperationException>();
        fixture.Preferences.Writes.Should().Be(0);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal Fixture()
        {
            Admission = new(Store, Store, new HostTaskCoordinator(Store));
            CallPolicy = new(new Call());
            Service = new(Preferences, Policy, Admission, Audit);
        }

        internal AudioControlTestStore Store { get; } = new();
        internal Preferences Preferences { get; } = new();
        internal SessionRetentionPolicy Policy { get; } = new();
        internal Audit Audit { get; } = new();
        internal bool Eligible { get; set; } = true;
        internal DiagnosticRetentionAdmission Admission { get; }
        internal CallCommunicationPolicy CallPolicy { get; }
        internal SessionRetentionConfigurationService Service { get; }
        internal Task<bool> Save(SessionRetentionSettings? settings, CancellationToken? token = null) =>
            Service.SaveAsync(settings, CallPolicy, CallPolicy.Current.Revision, () => Eligible,
                token ?? TestContext.Current.CancellationToken);
        public async ValueTask DisposeAsync() { CallPolicy.Dispose(); await Admission.DisposeAsync(); }
    }

    private sealed class Preferences : ISessionRetentionPreferences
    {
        internal SessionRetentionSettings? Value { get; set; }
        internal bool Pending { get; private set; }
        internal int Writes { get; private set; }
        internal Exception? ReadFailure { get; set; }
        internal Exception? BeginFailure { get; set; }
        internal Exception? WriteFailure { get; set; }
        internal Exception? ConfirmFailure { get; set; }
        internal Action? AfterSave { get; set; }
        internal Action? AfterConfirm { get; set; }
        public SessionRetentionSettings? Load()
        {
            if (Pending) { throw new InvalidDataException("Unconfirmed preference."); }
            return ReadBack();
        }
        public SessionRetentionSettings? ReadBack() { if (ReadFailure is { } failure) { throw failure; } return Value; }
        public void BeginWrite() { if (BeginFailure is { } failure) { throw failure; } Pending = true; }
        public void ConfirmWrite() { if (ConfirmFailure is { } failure) { throw failure; } Pending = false; AfterConfirm?.Invoke(); }
        public void Save(SessionRetentionSettings settings)
        {
            if (WriteFailure is { } failure) { throw failure; }
            Value = settings;
            Writes++;
            AfterSave?.Invoke();
        }
        public void Reset() { Value = null; Writes++; AfterSave?.Invoke(); }
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal Action<SecurityAuditEvent>? BeforeWrite { get; set; }
        public void Write(SecurityAuditEvent value) { BeforeWrite?.Invoke(value); HostActivity.RequireCurrent(); Events.Add(value); }
    }

    private sealed class Call : ICallStateService
    {
        public CallState CurrentState => CallState.Clear;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged { add { } remove { } }
    }
}
