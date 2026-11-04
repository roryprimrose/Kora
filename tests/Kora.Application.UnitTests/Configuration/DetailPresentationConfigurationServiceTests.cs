using System.Diagnostics;
using System.Globalization;
using AwesomeAssertions;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class DetailPresentationConfigurationServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public DetailPresentationConfigurationServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task ReviewCancelConfirmResetAndRestartUseOneAuditedPreferenceWithoutReplay()
    {
        await using var f = new Fixture();
        f.Service.CaptureOfferEpoch().Should().BeNull();
        f.Service.Observe();
        f.Service.Description.Should().Contain("Offer (unsaved default)");
        var firstOffer = f.Service.CaptureOfferEpoch();
        firstOffer.Should().NotBeNull();
        f.Service.IsOfferCurrent(null).Should().BeFalse();
        await f.Service.InspectAsync(RequestOrigin.LocalUi, () => f.Eligible, f.Token);
        f.Service.CaptureOfferEpoch().Should().Be(firstOffer, "inspection is not a new presentation choice");
        var cancelled = await f.Review(DetailPresentationMode.LinkOnly);
        cancelled.Description.Should().Contain("Offer (unsaved default) -> LinkOnly").And.Contain("Confirm separately");
        f.Service.Cancel();
        await f.InvokingConfirm(cancelled).Should().ThrowAsync<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
        await f.Set(DetailPresentationMode.LinkOnly);
        f.Service.CaptureOfferEpoch().Should().BeNull();
        f.Preferences.Pending.Should().BeFalse();
        var restarted = f.Create();
        restarted.Observe();
        restarted.CaptureOfferEpoch().Should().BeNull();
        var reset = await f.Review(DetailPresentationPreference.Default);
        f.Preferences.Value.Should().Be(DetailPresentationMode.LinkOnly, "reset is also reviewed, not immediately written");
        await f.Confirm(reset);
        f.Service.IsOfferCurrent(firstOffer).Should().BeFalse("restoring Offer never resurrects old publication authority");
        f.Service.CaptureOfferEpoch().Should().NotBeNull();
        f.Preferences.Value.Should().Be(DetailPresentationMode.Offer);
        f.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested,
            SecurityAuditOutcome.Succeeded, SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Audit.Events.Should().OnlyContain(item => item.ActionId == "configuration.detail-presentation"
            && item.TargetId == "preferences.device-local");
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("host")]
    [InlineData("source")]
    [InlineData("session")]
    [InlineData("revision")]
    [InlineData("replaced")]
    [InlineData("foreign-owner")]
    [InlineData("channel")]
    [InlineData("model")]
    [InlineData("voice-origin")]
    [InlineData("cancelled")]
    public async Task StaleOrForeignReviewCannotWrite(string boundary)
    {
        await using var f = new Fixture();
        f.Service.Observe();
        var candidate = await f.Review(DetailPresentationMode.LinkOnly);
        if (boundary is "expired") { f.Time.Now = f.Time.Now.AddSeconds(60); }
        if (boundary is "host") { f.Eligible = false; }
        if (boundary is "source") { f.Preferences.Value = DetailPresentationMode.Offer; }
        if (boundary is "session") { f.Store.Authority = f.Store.Authority! with { Generation = new(2) }; }
        if (boundary is "revision") { f.Service.HoldUnavailable(); f.Service.Observe(); }
        if (boundary is "replaced") { await f.Review(DetailPresentationMode.Offer); }
        var service = boundary is "foreign-owner" ? f.Create() : f.Service;
        service.Observe();
        using var cancelled = new CancellationTokenSource();
        if (boundary is "cancelled") { cancelled.Cancel(); }
        var confirm = () => service.ConfirmAsync(candidate,
            boundary is "channel" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi,
            boundary is "model" ? SecurityAuditInitiator.ModelSuggestion
                : boundary is "voice-origin" or "channel" ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            f.Policy, () => f.Eligible, cancelled.Token);
        await confirm.Should().ThrowAsync<Exception>();
        f.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(CallState.Active)]
    [InlineData(CallState.Unknown)]
    public async Task ProtectedCallsDenyOriginalVoiceReviewButAdmitFreshNativeReview(CallState state)
    {
        await using var f = new Fixture();
        f.Service.Observe();
        f.Call.State = state;
        var voice = () => f.Review(DetailPresentationMode.LinkOnly, RequestOrigin.ActivatedVoice);
        await voice.Should().ThrowAsync<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(0);
        await f.Set(DetailPresentationMode.LinkOnly);
        f.Preferences.Value.Should().Be(DetailPresentationMode.LinkOnly);
    }

    [Fact]
    public async Task CallRevisionChangedAfterReviewIsDeniedWithoutWrite()
    {
        await using var f = new Fixture();
        var candidate = await f.Review(DetailPresentationMode.LinkOnly, RequestOrigin.ActivatedVoice);
        f.Call.State = CallState.Active;
        (await f.Confirm(candidate, RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand)).Should().BeFalse();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
    }

    [Theory]
    [InlineData("load")]
    [InlineData("requested")]
    [InlineData("audit-drift")]
    [InlineData("context")]
    [InlineData("save")]
    [InlineData("access")]
    [InlineData("readback")]
    [InlineData("terminal")]
    [InlineData("receipt")]
    [InlineData("late-host")]
    [InlineData("late-readback")]
    [InlineData("confirm")]
    [InlineData("confirmed-readback")]
    [InlineData("confirmed-host")]
    public async Task FailedEvidenceNeverPublishesSuccessOrSilentlyRecovers(string boundary)
    {
        await using var f = new Fixture();
        var candidate = await f.Review(DetailPresentationMode.LinkOnly);
        f.Preferences.LoadFailure = boundary is "load" ? new InvalidDataException() : null;
        f.Preferences.SaveFailure = boundary switch { "save" => new IOException(), "access" => new UnauthorizedAccessException(), _ => null };
        f.Preferences.AfterSave = () => { if (boundary is "readback") { f.Preferences.Value = DetailPresentationMode.Offer; } };
        f.Preferences.ConfirmFailure = boundary is "confirm" ? new IOException() : null;
        f.Preferences.AfterConfirm = () =>
        {
            if (boundary is "confirmed-readback") { f.Preferences.Value = DetailPresentationMode.Offer; }
            if (boundary is "confirmed-host") { f.Eligible = false; }
        };
        f.Audit.BeforeWrite = item =>
        {
            if (item.Outcome == SecurityAuditOutcome.Requested)
            {
                if (boundary is "requested") { throw new IOException(); }
                if (boundary is "audit-drift") { f.Preferences.Value = DetailPresentationMode.Offer; }
                if (boundary is "context")
                { Kora.Core.Diagnostics.HostActivity.RequireCurrent().Activity!.Stop(); }
            }
            if (boundary is "terminal" && item.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException(); }
        };
        f.Store.FailTerminal = boundary is "receipt";
        f.Store.BeforeCommit = task =>
        {
            if (task.State != HostTaskState.Succeeded) { return; }
            if (boundary is "late-host") { f.Eligible = false; }
            if (boundary is "late-readback") { f.Preferences.Value = DetailPresentationMode.Offer; }
        };
        await f.InvokingConfirm(candidate).Should().ThrowAsync<Exception>();
        f.Service.CaptureOfferEpoch().Should().BeNull();
        f.Service.Description.Should().Contain("unavailable");
        if (boundary is "load" or "requested" or "audit-drift" or "context")
        { f.Preferences.Writes.Should().Be(0); }
        else
        {
            f.Preferences.Pending.Should().BeTrue();
            f.Create().Invoking(item => item.Observe()).Should().Throw<InvalidDataException>();
        }
    }

    [Fact]
    public async Task ConcurrentOperationsAndLateAdmissionCannotPublishAfterDisposal()
    {
        await using var f = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<Kora.Core.Authorization.WorkSessionAuthorization>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.PendingCreation = release.Task;
        f.Store.BeforeControlIntent = _ => entered.TrySetResult();
        var pending = f.Review(DetailPresentationMode.LinkOnly);
        await entered.Task.WaitAsync(f.Token);
        f.Service.Invoking(item => item.Observe()).Should().Throw<InvalidOperationException>();
        var second = () => f.Review(DetailPresentationMode.Offer);
        await second.Should().ThrowAsync<InvalidOperationException>();
        await f.Admission.DisposeAsync();
        release.TrySetResult(new(new(Guid.NewGuid()), new(1), true));
        Func<Task> finish = async () => { await pending; };
        await finish.Should().ThrowAsync<OperationCanceledException>();
        f.Preferences.Writes.Should().Be(0);
        f.Service.CaptureOfferEpoch().Should().BeNull();
    }

    [Theory]
    [InlineData(false, "cancelled")]
    [InlineData(false, "host")]
    [InlineData(true, "cancelled")]
    [InlineData(true, "host")]
    [InlineData(true, "revision")]
    public async Task DetailPresentationTerminalReceiptCannotRenewWithdrawnObservationOrReview(bool reviewing, string boundary)
    {
        await using var f = new Fixture();
        await f.Service.InspectAsync(RequestOrigin.LocalUi, () => f.Eligible, f.Token);
        using var cancellation = new CancellationTokenSource();
        f.Store.BeforeCommit = record =>
        {
            if (!record.IsTerminal) { return; }
            if (boundary is "cancelled") { cancellation.Cancel(); }
            if (boundary is "host") { f.Eligible = false; }
            if (boundary is "revision") { f.Service.HoldUnavailable(); }
        };
        Func<Task> operation = async () =>
        {
            if (reviewing)
            {
                await f.Service.ReviewAsync(DetailPresentationMode.LinkOnly, RequestOrigin.LocalUi,
                    f.Policy.Current.Revision, f.Policy, () => f.Eligible, cancellation.Token);
            }
            else { await f.Service.InspectAsync(RequestOrigin.LocalUi, () => f.Eligible, cancellation.Token); }
        };
        await operation.Should().ThrowAsync<Exception>();
        f.Service.CaptureOfferEpoch().Should().BeNull();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        internal Fixture()
        {
            Admission = new(Store, Store, new(Store)); Policy = new(Call); Service = Create();
        }
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        internal bool Eligible { get; set; } = true;
        internal Preferences Preferences { get; } = new();
        internal ConversationalReplyConfigurationServiceTests.Audit Audit { get; } = new();
        internal ConversationalReplyConfigurationServiceTests.Call Call { get; } = new();
        internal AudioControlTestStore Store { get; } = new();
        internal Clock Time { get; } = new();
        internal AudioControlAdmission Admission { get; }
        internal CallCommunicationPolicy Policy { get; }
        internal DetailPresentationConfigurationService Service { get; }
        internal DetailPresentationConfigurationService Create() => new(Preferences, Admission, Audit, Time);
        internal Task<DetailPresentationReview> Review(DetailPresentationMode mode, RequestOrigin origin = RequestOrigin.LocalUi) =>
            Service.ReviewAsync(mode, origin, Policy.Current.Revision, Policy, () => Eligible, Token);
        internal Task<bool> Confirm(DetailPresentationReview review, RequestOrigin origin = RequestOrigin.LocalUi,
            SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser) =>
            Service.ConfirmAsync(review, origin, initiator, Policy, () => Eligible, Token);
        internal Func<Task> InvokingConfirm(DetailPresentationReview review) => async () => { await Confirm(review); };
        internal async Task Set(DetailPresentationMode mode) { (await Confirm(await Review(mode))).Should().BeTrue(); }
        public async ValueTask DisposeAsync() { Policy.Dispose(); await Admission.DisposeAsync(); }
    }

    internal sealed class Preferences : IDetailPresentationPreferences
    {
        internal DetailPresentationMode? Value { get; set; }
        internal bool Pending { get; private set; }
        internal int Writes { get; private set; }
        internal Exception? LoadFailure { get; set; }
        internal Exception? SaveFailure { get; set; }
        internal Exception? ConfirmFailure { get; set; }
        internal Action? AfterSave { get; set; }
        internal Action? AfterConfirm { get; set; }
        public DetailPresentationMode? Load() => LoadFailure is { } failure ? throw failure
            : Pending ? throw new InvalidDataException() : Value;
        public DetailPresentationMode? ReadBack() => Value;
        public void BeginWrite() => Pending = true;
        public void Save(DetailPresentationMode mode)
        {
            if (SaveFailure is { } failure) { throw failure; }
            Value = mode; Writes++; AfterSave?.Invoke();
        }
        public void ConfirmWrite()
        {
            if (ConfirmFailure is { } failure) { throw failure; }
            Pending = false; AfterConfirm?.Invoke();
        }
    }
    internal sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-10-30T00:00:00Z", CultureInfo.InvariantCulture);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
