using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class LogonStartupConfigurationServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public LogonStartupConfigurationServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public async Task ReviewAndCancelAreInertAndSeparateConfirmationSavesExactIntent(bool? value)
    {
        await using var f = new Fixture();
        f.Service.Get().Available.Should().BeFalse();
        (await f.Inspect()).Should().Be(new LogonStartupState(null, false, true, null));
        f.Service.Get().Description.Should().Contain("default Off");
        var cancelled = await f.Review(value);
        cancelled.Description.Should().Contain("No change yet").And.Contain("fresh launch");
        f.Registration.Writes.Should().Be(0);
        f.Preferences.Writes.Should().Be(0);
        f.Service.Cancel();
        await f.Invoking(item => item.Confirm(cancelled)).Should().ThrowAsync<InvalidOperationException>();
        var review = await f.Review(value);
        (await f.Confirm(review)).Should().BeTrue();
        f.Preferences.Value.Should().Be(value);
        f.Preferences.Pending.Should().BeFalse();
        f.Registration.State.Enabled.Should().Be(value == true);
        f.Service.Get().Should().Be(new LogonStartupState(value, value == true, true, null));
        f.Service.Get().Description.Should().Contain("Registration is not application readiness");
        f.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Audit.Events.Should().OnlyContain(item => string.Equals(item.ActionId, "configuration.start-at-logon", StringComparison.Ordinal));
        await f.Invoking(item => item.Confirm(review)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("install")]
    [InlineData("registry")]
    [InlineData("saved")]
    [InlineData("expired")]
    [InlineData("host")]
    [InlineData("source-host")]
    [InlineData("session")]
    [InlineData("origin")]
    [InlineData("model")]
    [InlineData("voice-origin")]
    [InlineData("cancelled")]
    [InlineData("replaced")]
    public async Task ChangedOrUnconfirmedAuthorityCannotMutate(string boundary)
    {
        await using var f = new Fixture();
        var sourceEligible = true;
        var review = await f.Service.ReviewAsync(true, RequestOrigin.LocalUi, f.Policy.Current.Revision,
            f.Policy, () => sourceEligible, TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        switch (boundary)
        {
            case "install": f.Registration.State = f.Registration.State with { Identity = "replacement" }; break;
            case "registry": f.Registration.State = f.Registration.State with { Enabled = true }; break;
            case "saved": f.Preferences.Value = false; break;
            case "expired": f.Time.Now += TimeSpan.FromSeconds(60); break;
            case "host": f.Eligible = false; break;
            case "source-host": sourceEligible = false; break;
            case "session": f.Store.Authority = f.Store.Authority! with { Generation = new(2) }; break;
            case "cancelled": await cancellation.CancelAsync(); break;
            case "replaced": await f.Review(false); break;
        }
        var apply = () => f.Service.ConfirmAsync(review,
            boundary is "origin" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi,
            boundary is "model" ? SecurityAuditInitiator.ModelSuggestion : boundary is "voice-origin"
                ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.TypedCommand,
            f.Policy, () => f.Eligible, cancellation.Token);
        await apply.Should().ThrowAsync<Exception>();
        f.Registration.Writes.Should().Be(0);
        f.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("call")]
    [InlineData("load")]
    [InlineData("inspect")]
    [InlineData("late")]
    public async Task ReviewFailureIsExplicitAndHasNoEffects(string boundary)
    {
        await using var f = new Fixture();
        if (boundary is "unavailable") { f.Registration.State = f.Registration.State with { Available = false, UnavailableReason = "foreign" }; }
        if (boundary is "call") { f.Call.State = Kora.Core.Communication.CallState.Active; }
        if (boundary is "load") { f.Preferences.LoadFailure = new InvalidDataException(); }
        if (boundary is "inspect") { f.Registration.Failure = new IOException(); }
        if (boundary is "late") { f.Store.AfterOperation = () => f.Eligible = false; }
        var review = () => f.Review(true, boundary is "call" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        await review.Should().ThrowAsync<Exception>();
        f.Service.Get().Available.Should().BeFalse();
        f.Service.Get().Description.Should().Contain("unconfirmed");
        f.Registration.Writes.Should().Be(0);
        f.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("load")]
    [InlineData("late")]
    [InlineData("unavailable")]
    public async Task InspectionNeverReinterpretsMalformedUnknownOrLateState(string boundary)
    {
        await using var f = new Fixture();
        if (boundary is "load") { f.Preferences.LoadFailure = new InvalidDataException(); }
        if (boundary is "late") { f.Store.AfterOperation = () => f.Eligible = false; }
        if (boundary is "unavailable")
        {
            f.Registration.State = new(false, null, "foreign", "unowned");
            (await f.Inspect()).Should().Be(new LogonStartupState(null, null, false, "unowned"));
        }
        else { await f.Invoking(item => item.Inspect()).Should().ThrowAsync<Exception>(); }
        f.Service.Get().Available.Should().BeFalse();
        f.Preferences.Writes.Should().Be(0);
        f.Registration.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("requested")]
    [InlineData("audit-drift")]
    [InlineData("registry-write")]
    [InlineData("save")]
    [InlineData("readback")]
    [InlineData("identity-readback")]
    [InlineData("effective-readback")]
    [InlineData("unavailable-readback")]
    [InlineData("terminal")]
    [InlineData("receipt")]
    [InlineData("late-host")]
    [InlineData("late-readback")]
    [InlineData("confirm")]
    [InlineData("confirmed-readback")]
    [InlineData("confirmed-host")]
    public async Task FailureRetainsUnconfirmedHoldWithoutRetryOrRollback(string boundary)
    {
        await using var f = new Fixture();
        var review = await f.Review(true);
        f.Audit.BeforeWrite = item =>
        {
            if (boundary is "requested" && item.Outcome == SecurityAuditOutcome.Requested
                || boundary is "terminal" && item.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException(); }
            if (boundary is "audit-drift" && item.Outcome == SecurityAuditOutcome.Requested) { f.Preferences.Value = false; }
        };
        if (boundary is "registry-write") { f.Registration.BeforeWrite = () => throw new IOException(); }
        if (boundary is "save") { f.Preferences.SaveFailure = new UnauthorizedAccessException(); }
        if (boundary is "confirm") { f.Preferences.ConfirmFailure = new IOException(); }
        f.Preferences.AfterSave = () =>
        {
            if (boundary is "readback") { f.Preferences.Value = false; }
            if (boundary is "identity-readback") { f.Registration.State = f.Registration.State with { Identity = "drift" }; }
            if (boundary is "effective-readback") { f.Registration.State = f.Registration.State with { Enabled = false }; }
            if (boundary is "unavailable-readback") { f.Registration.State = f.Registration.State with { Available = false }; }
        };
        f.Preferences.AfterConfirm = () =>
        {
            if (boundary is "confirmed-readback") { f.Preferences.Value = false; }
            if (boundary is "confirmed-host") { f.Eligible = false; }
        };
        f.Store.FailTerminal = boundary is "receipt";
        f.Store.BeforeCommit = task =>
        {
            if (task.State != HostTaskState.Succeeded) { return; }
            if (boundary is "late-host") { f.Eligible = false; }
            if (boundary is "late-readback") { f.Preferences.Value = false; }
        };
        await f.Invoking(item => item.Confirm(review)).Should().ThrowAsync<Exception>();
        f.Service.Get().Available.Should().BeFalse();
        f.Service.Get().Effective.Should().BeNull();
        f.Registration.Writes.Should().BeLessThanOrEqualTo(1);
        if (boundary is not ("requested" or "audit-drift")) { f.Preferences.Pending.Should().BeTrue(); }
    }

    [Fact]
    public async Task CallDriftDeniesConfirmationAndConcurrentReentryIsRefused()
    {
        await using var f = new Fixture();
        var review = await f.Review(true);
        var checkedBusy = false;
        f.Audit.BeforeWrite = _ =>
        {
            if (checkedBusy) { return; }
            checkedBusy = true;
            Func<Task> inspect = async () => await f.Inspect();
            var reentry = inspect();
            reentry.IsFaulted.Should().BeTrue();
            reentry.Exception!.InnerException.Should().BeOfType<InvalidOperationException>();
            f.Call.State = Kora.Core.Communication.CallState.Active;
        };
        (await f.Confirm(review)).Should().BeFalse();
        f.Registration.Writes.Should().Be(0);
        f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        public Fixture() { Admission = new(Store, Store, new(Store)); Policy = new(Call); Service = new(Preferences, Registration, Admission, Audit, Time); }
        public AudioControlTestStore Store { get; } = new();
        public Preferences Preferences { get; } = new();
        public Registration Registration { get; } = new();
        public ConversationalReplyConfigurationServiceTests.Audit Audit { get; } = new();
        public ConversationalReplyConfigurationServiceTests.Call Call { get; } = new();
        public Clock Time { get; } = new();
        public LogonStartupAdmission Admission { get; }
        public CallCommunicationPolicy Policy { get; }
        public LogonStartupConfigurationService Service { get; }
        public bool Eligible { get; set; } = true;
        public Task<LogonStartupState> Inspect() => Service.InspectAsync(RequestOrigin.LocalUi, () => Eligible, TestContext.Current.CancellationToken);
        public Task<LogonStartupReview> Review(bool? value, RequestOrigin origin = RequestOrigin.LocalUi) =>
            Service.ReviewAsync(value, origin, Policy.Current.Revision, Policy, () => Eligible, TestContext.Current.CancellationToken);
        public Task<bool> Confirm(LogonStartupReview review) => Service.ConfirmAsync(review, RequestOrigin.LocalUi,
            SecurityAuditInitiator.TypedCommand, Policy, () => Eligible, TestContext.Current.CancellationToken);
        public async ValueTask DisposeAsync() { Policy.Dispose(); await Admission.DisposeAsync(); }
    }
    internal sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    internal sealed class Preferences : ILogonStartupPreferences
    {
        public bool? Value { get; set; }
        public bool Pending { get; private set; }
        public int Writes { get; private set; }
        public Exception? LoadFailure { get; set; }
        public Exception? SaveFailure { get; set; }
        public Exception? ConfirmFailure { get; set; }
        public Action? AfterSave { get; set; }
        public Action? AfterConfirm { get; set; }
        public bool? Load() => LoadFailure is { } failure ? throw failure : Pending ? throw new InvalidDataException() : Value;
        public bool? ReadBack() => Value;
        public void BeginWrite() => Pending = true;
        public void Save(bool? enabled) { if (SaveFailure is { } failure) { throw failure; } Value = enabled; Writes++; AfterSave?.Invoke(); }
        public void ConfirmWrite() { if (ConfirmFailure is { } failure) { throw failure; } Pending = false; AfterConfirm?.Invoke(); }
    }
    internal sealed class Registration : ILogonStartupRegistration
    {
        public LogonStartupRegistration State { get; set; } = new(true, false, "installed-exact-image", null);
        public int Writes { get; private set; }
        public Exception? Failure { get; set; }
        public Action? BeforeWrite { get; set; }
        public LogonStartupRegistration Inspect() => Failure is { } failure ? throw failure : State;
        public void Apply(bool enabled, LogonStartupRegistration reviewed, Func<bool> admitted)
        {
            BeforeWrite?.Invoke();
            if (!admitted() || reviewed != State) { throw new InvalidOperationException(); }
            Writes++;
            State = State with { Enabled = enabled };
        }
    }
}
