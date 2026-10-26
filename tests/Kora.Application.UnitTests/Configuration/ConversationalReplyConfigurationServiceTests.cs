using AwesomeAssertions;
using System.Diagnostics;
using Kora.Core.Diagnostics;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class ConversationalReplyConfigurationServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public ConversationalReplyConfigurationServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task Default_off_saved_opt_in_audit_restart_and_explicit_off_share_one_revisioned_preference()
    {
        await using var f = new Fixture();
        f.Service.Get().Should().BeNull();
        var changes = 0;
        f.Service.Changed += (_, _) => changes++;
        f.Service.Observe();
        f.Service.Get()!.Enabled.Should().BeFalse();
        await f.Set(true);
        f.Service.Get()!.Enabled.Should().BeTrue();
        f.Preferences.Pending.Should().BeFalse();
        var restarted = f.Create();
        restarted.Observe();
        restarted.Get()!.Enabled.Should().BeTrue();
        await f.Set(false);
        f.Service.Get()!.Enabled.Should().BeFalse();
        f.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded,
            SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Audit.Events.Should().OnlyContain(item => string.Equals(item.ActionId, "configuration.conversational-replies", StringComparison.Ordinal));
        changes.Should().BeGreaterThan(2);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("stale")]
    [InlineData("saved")]
    [InlineData("host")]
    [InlineData("model")]
    [InlineData("voice-origin")]
    public async Task Stale_or_nonoriginal_inputs_have_no_preference_write(string boundary)
    {
        await using var f = new Fixture();
        f.Service.Observe();
        var revision = f.Service.Get()!.Revision;
        if (boundary is "unknown") { f.Service.HoldUnavailable(); }
        if (boundary is "stale") { f.Service.Observe(); }
        if (boundary is "saved") { f.Preferences.Value = true; }
        if (boundary is "host") { f.Eligible = false; }
        var apply = () => f.Service.SetAsync(true, revision, f.Policy.Current.Revision, RequestOrigin.LocalUi,
            boundary is "model" ? SecurityAuditInitiator.ModelSuggestion
                : boundary is "voice-origin" ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            f.Policy, () => f.Eligible, TestContext.Current.CancellationToken);
        await apply.Should().ThrowAsync<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Protected_call_refuses_activated_voice_but_native_original_choice_remains_available()
    {
        await using var f = new Fixture();
        f.Service.Observe();
        f.Call.State = CallState.Active;
        await f.Set(true, RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand);
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
        await f.Set(true);
        f.Service.Get()!.Enabled.Should().BeTrue();
        f.Audit.Events.Last().Initiator.Should().Be(SecurityAuditInitiator.LocalUser);
    }

    [Theory]
    [InlineData("load")]
    [InlineData("save")]
    [InlineData("access")]
    [InlineData("readback")]
    [InlineData("requested")]
    [InlineData("terminal")]
    [InlineData("receipt")]
    [InlineData("late-readback")]
    [InlineData("late-host")]
    [InlineData("confirm")]
    [InlineData("confirmed-readback")]
    [InlineData("confirmed-load")]
    public async Task Unconfirmed_storage_or_audit_never_enables_capture_or_silently_recovers(string boundary)
    {
        await using var f = new Fixture();
        f.Service.Observe();
        await f.Admission.RunAsync(RequestOrigin.LocalUi, static () => true, (_, _) => true, TestContext.Current.CancellationToken);
        f.Preferences.Failure = boundary switch
        {
            "save" => new IOException(),
            "access" => new UnauthorizedAccessException(),
            _ => null,
        };
        f.Preferences.AfterSave = () => { if (boundary is "readback") { f.Preferences.Value = false; } };
        f.Preferences.LoadFailure = boundary is "load" ? new InvalidDataException() : null;
        f.Audit.BeforeWrite = item =>
        {
            if (boundary is "requested" && item.Outcome == SecurityAuditOutcome.Requested
                || boundary is "terminal" && item.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException(); }
        };
        f.Store.FailTerminal = boundary is "receipt";
        f.Store.BeforeCommit = task =>
        {
            if (task.State != HostTaskState.Succeeded) { return; }
            if (boundary is "late-readback") { f.Preferences.Value = false; }
            if (boundary is "late-host") { f.Eligible = false; }
        };
        f.Preferences.ConfirmFailure = boundary is "confirm" ? new IOException() : null;
        f.Preferences.AfterConfirm = () =>
        {
            if (boundary is "confirmed-readback") { f.Preferences.Value = false; }
            if (boundary is "confirmed-load") { f.Preferences.LoadFailure = new IOException(); }
        };
        var apply = () => f.Set(true);
        await apply.Should().ThrowAsync<Exception>();
        if (boundary is not "load")
        {
            f.Service.Get().Should().BeNull();
            if (boundary is not "requested") { f.Preferences.Pending.Should().BeTrue(); }
        }
        f.Service.Invoking(service =>
        {
            f.Preferences.LoadFailure = new InvalidDataException();
            service.Observe();
        }).Should().Throw<InvalidDataException>();
        f.Service.Get().Should().BeNull();
    }

    [Fact]
    public async Task Concurrent_observe_is_refused_while_required_audit_is_committing()
    {
        await using var f = new Fixture();
        f.Service.Observe();
        var observed = false;
        f.Audit.BeforeWrite = item =>
        {
            if (item.Outcome != SecurityAuditOutcome.Requested) { return; }
            observed = true;
            f.Service.Invoking(service => service.Observe()).Should().Throw<InvalidOperationException>();
        };
        await f.Set(true);
        observed.Should().BeTrue();
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        public Fixture() { Admission = new(Store, Store, new(Store)); Policy = new(Call); Service = Create(); }
        public bool Eligible { get; set; } = true;
        public Preferences Preferences { get; } = new();
        public Audit Audit { get; } = new();
        public AudioControlTestStore Store { get; } = new();
        public Call Call { get; } = new();
        public AudioControlAdmission Admission { get; }
        public CallCommunicationPolicy Policy { get; }
        public ConversationalReplyConfigurationService Service { get; }
        public ConversationalReplyConfigurationService Create() => new(Preferences, Admission, Audit);
        public Task<bool> Set(bool enabled, RequestOrigin origin = RequestOrigin.LocalUi,
            SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser) => Service.SetAsync(enabled,
                Service.Get()!.Revision, Policy.Current.Revision, origin, initiator, Policy, () => Eligible, TestContext.Current.CancellationToken);
        public async ValueTask DisposeAsync() { Policy.Dispose(); await Admission.DisposeAsync(); }
    }

    internal sealed class Preferences : IConversationalReplyPreferences
    {
        public bool? Value { get; set; }
        public bool Pending { get; private set; }
        public int Writes { get; private set; }
        public Exception? Failure { get; set; }
        public Exception? LoadFailure { get; set; }
        public Exception? ConfirmFailure { get; set; }
        public Action? AfterSave { get; set; }
        public Action? AfterConfirm { get; set; }
        public bool? Load() => LoadFailure is { } failure ? throw failure : Pending ? throw new InvalidDataException() : Value;
        public bool? ReadBack() => Value;
        public void BeginWrite() => Pending = true;
        public void Save(bool enabled) { if (Failure is { } failure) { throw failure; } Value = enabled; Writes++; AfterSave?.Invoke(); }
        public void ConfirmWrite() { if (ConfirmFailure is { } failure) { throw failure; } Pending = false; AfterConfirm?.Invoke(); }
    }
    internal sealed class Audit : ISecurityAuditLog
    {
        public List<SecurityAuditEvent> Events { get; } = [];
        public Action<SecurityAuditEvent>? BeforeWrite { get; set; }
        public void Write(SecurityAuditEvent item) { BeforeWrite?.Invoke(item); Events.Add(item); }
    }
    internal sealed class Call : ICallStateService
    {
        private CallState state = CallState.Clear;
        public CallState State { get => state; set { state = value; StateChanged?.Invoke(this, new(value)); } }
        public CallState CurrentState => state;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
    }
}
