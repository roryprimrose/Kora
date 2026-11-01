using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class StartupPresentationConfigurationServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public StartupPresentationConfigurationServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData(StartupPresentation.Tray)]
    [InlineData(StartupPresentation.Workspace)]
    [InlineData(null)]
    public async Task ReviewCancelConfirmReadbackAndFutureReadAreDistinct(StartupPresentation? value)
    {
        await using var f = new Fixture();
        f.Service.Status.Should().Contain("not been inspected");
        await f.Service.InspectAsync(() => f.Eligible, TestContext.Current.CancellationToken);
        f.Service.Status.Should().Contain("no saved override");
        var cancelled = await f.Review(value);
        cancelled.Description.Should().Contain("No change yet").And.Contain("logon").And.Contain("No launch now");
        f.Preferences.Writes.Should().Be(0);
        f.Service.Cancel();
        await f.Invoking(x => x.Confirm(cancelled)).Should().ThrowAsync<InvalidOperationException>();
        var review = await f.Review(value);
        await f.Confirm(review);
        f.Preferences.Value.Should().Be(value);
        f.Preferences.Pending.Should().BeFalse();
        (await f.Service.ReadStartupAsync(TestContext.Current.CancellationToken)).Should().Be(value);
        f.Service.Status.Should().Contain("Last confirmed").And.Contain("not a startup readiness claim");
        f.Audit.Events.Select(x => x.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Audit.Events.Should().OnlyContain(x => x.ActionId == "configuration.manual-startup-presentation"
            && x.Initiator == SecurityAuditInitiator.LocalUser);
        await f.Invoking(x => x.Confirm(review)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("host")]
    [InlineData("source")]
    [InlineData("saved")]
    [InlineData("session")]
    [InlineData("expired")]
    [InlineData("cancel")]
    [InlineData("replaced")]
    [InlineData("voice")]
    public async Task StaleReviewAndOriginalVoiceCannotMutate(string boundary)
    {
        await using var f = new Fixture();
        var source = true;
        var review = await f.Service.ReviewAsync(StartupPresentation.Workspace, f.Policy.Current.Revision,
            f.Policy, () => source, TestContext.Current.CancellationToken);
        using var token = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        switch (boundary)
        {
            case "host": f.Eligible = false; break;
            case "source": source = false; break;
            case "saved": f.Preferences.Value = StartupPresentation.Tray; break;
            case "session": f.Store.Authority = f.Store.Authority! with { Generation = new(2) }; break;
            case "expired": f.Clock.Now += TimeSpan.FromSeconds(60); break;
            case "cancel": await token.CancelAsync(); break;
            case "replaced": await f.Review(null); break;
        }
        using var origin = boundary is "voice"
            ? Kora.Core.Diagnostics.HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
                Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request) : null;
        Func<Task> confirm = () => f.Service.ConfirmAsync(review, f.Policy, () => f.Eligible, token.Token);
        await confirm.Should().ThrowAsync<Exception>();
        f.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("read")]
    [InlineData("late")]
    [InlineData("call")]
    [InlineData("voice")]
    public async Task InspectionAndReviewFailuresAreExplicitAndInert(string boundary)
    {
        await using var f = new Fixture();
        if (boundary is "read") { f.Preferences.LoadFailure = new InvalidDataException(); }
        if (boundary is "late") { f.Store.AfterOperation = () => f.Eligible = false; }
        if (boundary is "read" or "late")
        {
            Func<Task> inspect = () => f.Service.InspectAsync(() => f.Eligible, TestContext.Current.CancellationToken);
            await inspect.Should().ThrowAsync<Exception>();
            f.Eligible = true;
        }
        if (boundary is "call") { f.Call.State = Kora.Core.Communication.CallState.Active; }
        using var origin = boundary is "voice"
            ? Kora.Core.Diagnostics.HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
                Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request) : null;
        // Native policy may permit a call mutation; force its captured revision to be stale.
        Func<Task> review = async () => await f.Service.ReviewAsync(StartupPresentation.Workspace,
            boundary is "call" ? f.Policy.Current.Revision - 1 : f.Policy.Current.Revision, f.Policy, () => f.Eligible,
            TestContext.Current.CancellationToken);
        await review.Should().ThrowAsync<Exception>();
        f.Service.Status.Should().Contain("unavailable or unconfirmed");
        f.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("requested")]
    [InlineData("audit-drift")]
    [InlineData("begin")]
    [InlineData("save")]
    [InlineData("readback")]
    [InlineData("terminal")]
    [InlineData("receipt")]
    [InlineData("late")]
    [InlineData("confirm")]
    [InlineData("confirmed-value")]
    [InlineData("confirmed-host")]
    public async Task UncertainWritesRemainHeldWithoutRetryOrRollback(string boundary)
    {
        await using var f = new Fixture();
        var review = await f.Review(StartupPresentation.Workspace);
        f.Audit.BeforeWrite = e =>
        {
            if (boundary is "requested" && e.Outcome == SecurityAuditOutcome.Requested
                || boundary is "terminal" && e.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException(); }
            if (boundary is "audit-drift" && e.Outcome == SecurityAuditOutcome.Requested)
            { f.Preferences.Value = StartupPresentation.Tray; }
        };
        if (boundary is "begin") { f.Preferences.BeginFailure = new IOException(); }
        if (boundary is "save") { f.Preferences.SaveFailure = new IOException(); }
        if (boundary is "readback") { f.Preferences.AfterSave = () => f.Preferences.Value = StartupPresentation.Tray; }
        if (boundary is "receipt") { f.Store.FailTerminal = true; }
        if (boundary is "late") { f.Store.AfterOperation = () => f.Eligible = false; }
        if (boundary is "confirm") { f.Preferences.ConfirmFailure = new IOException(); }
        if (boundary is "confirmed-value") { f.Preferences.AfterConfirm = () => f.Preferences.Value = StartupPresentation.Tray; }
        if (boundary is "confirmed-host") { f.Preferences.AfterConfirm = () => f.Eligible = false; }
        await f.Invoking(x => x.Confirm(review)).Should().ThrowAsync<Exception>();
        f.Service.Status.Should().Contain("unconfirmed");
        if (boundary is not ("requested" or "audit-drift" or "begin"))
        { f.Preferences.Pending.Should().BeTrue(); }
        f.Preferences.Writes.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task ReentryInvalidChoicesAndRequestedCallDriftDoNotWrite()
    {
        await using var f = new Fixture();
        Func<Task> invalid = async () => await f.Review((StartupPresentation)42);
        await invalid.Should().ThrowAsync<ArgumentOutOfRangeException>();
        var review = await f.Review(StartupPresentation.Workspace);
        f.Audit.BeforeWrite = e =>
        {
            if (e.Outcome != SecurityAuditOutcome.Requested) { return; }
            f.Service.InspectAsync(() => true, TestContext.Current.CancellationToken).IsFaulted.Should().BeTrue();
            f.Call.State = Kora.Core.Communication.CallState.Active;
        };
        await f.Invoking(x => x.Confirm(review)).Should().ThrowAsync<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
    }

    [Fact]
    public async Task StartupReadIsInertAndConfigurationChangesInvalidateItsCapturedEligibility()
    {
        await using var f = new Fixture();
        var initial = f.Service.CaptureStartupCurrent();
        initial().Should().BeTrue();
        (await f.Service.ReadStartupAsync(TestContext.Current.CancellationToken)).Should().BeNull();
        f.Service.Cancel();
        initial().Should().BeFalse();
        var reviewed = await f.Review(StartupPresentation.Workspace);
        var current = f.Service.CaptureStartupCurrent();
        current().Should().BeTrue();
        Func<bool>? held = null;
        f.Audit.BeforeWrite = e =>
        {
            if (e.Outcome == SecurityAuditOutcome.Requested)
            { held = f.Service.CaptureStartupCurrent(); held().Should().BeFalse(); current().Should().BeFalse(); }
        };
        await f.Confirm(reviewed);
        current().Should().BeFalse();
        held!().Should().BeFalse("capture during a configuration hold cannot revive");
        (await f.Service.ReadStartupAsync(TestContext.Current.CancellationToken)).Should().Be(StartupPresentation.Workspace);
        var writes = f.Preferences.Writes;
        f.Preferences.LoadFailure = new InvalidDataException("corrupt");
        await f.Service.Invoking(x => x.ReadStartupAsync(TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidDataException>();
        f.Preferences.Writes.Should().Be(writes);
        Action invalid = () => StartupPresentationReview.Describe((StartupPresentation)42);
        invalid.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("inspect")]
    [InlineData("review")]
    [InlineData("confirm")]
    public async Task CancelWhileStorageIsAdmittingTheOperationPreventsAWriteOrLateReview(string operation)
    {
        await using var f = new Fixture();
        var review = await f.Review(StartupPresentation.Workspace);
        f.Store.BeforeOperation = f.Service.Cancel;
        Func<Task> run = operation switch
        {
            "inspect" => () => f.Service.InspectAsync(() => f.Eligible, TestContext.Current.CancellationToken),
            "review" => async () => await f.Review(StartupPresentation.Tray),
            _ => () => f.Confirm(review),
        };
        await run.Should().ThrowAsync<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(0);
        f.Service.Status.Should().Contain("unconfirmed");
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        public Fixture()
        { Admission = new(Store, Store, new(Store)); Policy = new(Call); Service = new(Preferences, Admission, Audit, Clock); }
        internal AudioControlTestStore Store { get; } = new();
        internal Preferences Preferences { get; } = new();
        internal ConversationalReplyConfigurationServiceTests.Audit Audit { get; } = new();
        internal ConversationalReplyConfigurationServiceTests.Call Call { get; } = new();
        internal LogonStartupConfigurationServiceTests.Clock Clock { get; } = new();
        internal LogonStartupAdmission Admission { get; }
        internal CallCommunicationPolicy Policy { get; }
        internal StartupPresentationConfigurationService Service { get; }
        internal bool Eligible { get; set; } = true;
        internal Task<StartupPresentationReview> Review(StartupPresentation? value) =>
            Service.ReviewAsync(value, Policy.Current.Revision, Policy, () => Eligible, TestContext.Current.CancellationToken);
        internal Task Confirm(StartupPresentationReview review) =>
            Service.ConfirmAsync(review, Policy, () => Eligible, TestContext.Current.CancellationToken);
        public async ValueTask DisposeAsync() { Policy.Dispose(); await Admission.DisposeAsync(); }
    }
    internal sealed class Preferences : IStartupPresentationPreferences
    {
        public StartupPresentation? Value { get; set; }
        public bool Pending { get; private set; }
        public int Writes { get; private set; }
        public Exception? LoadFailure { get; set; }
        public Exception? BeginFailure { get; set; }
        public Exception? SaveFailure { get; set; }
        public Exception? ConfirmFailure { get; set; }
        public Action? AfterSave { get; set; }
        public Action? AfterConfirm { get; set; }
        public StartupPresentation? Load() => LoadFailure is { } failure ? throw failure : Pending ? throw new InvalidDataException() : Value;
        public StartupPresentation? ReadBack() => Value;
        public void BeginWrite() { Pending = true; if (BeginFailure is { } failure) { throw failure; } }
        public void Save(StartupPresentation? value) { if (SaveFailure is { } failure) { throw failure; } Value = value; Writes++; AfterSave?.Invoke(); }
        public void ConfirmWrite() { if (ConfirmFailure is { } failure) { throw failure; } Pending = false; AfterConfirm?.Invoke(); }
    }
}
