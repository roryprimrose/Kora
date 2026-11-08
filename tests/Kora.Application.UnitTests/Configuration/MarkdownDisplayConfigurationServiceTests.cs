using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class MarkdownDisplayConfigurationServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public MarkdownDisplayConfigurationServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task ObserveReviewCancelAuditedConfirmExactCaptureResetAndRestart()
    {
        await using var f = new Fixture();
        f.Service.Description.Should().Contain("unavailable");
        var unavailable = () => f.Service.CaptureAsync(() => true, f.Token);
        await unavailable.Should().ThrowAsync<InvalidOperationException>();
        await f.Service.ObserveAsync(f.Token);
        f.Service.Description.Should().Contain("Rendered (unsaved default)");
        (await f.Capture()).Should().Be(MarkdownDisplayMode.Rendered);
        await f.Service.InspectAsync(RequestOrigin.LocalUi, () => f.Eligible, f.Token);
        await f.Service.ObserveAsync(f.Token);
        var candidate = await f.Review(MarkdownDisplayMode.Source);
        candidate.Description.Should().Contain("Rendered (unsaved default) -> Source").And.Contain("Save separately");
        f.Preferences.Writes.Should().Be(0);
        (await f.Capture()).Should().Be(MarkdownDisplayMode.Rendered, "review is not saved intent");
        f.Service.Cancel();
        await new Func<Task>(async () => await f.Confirm(candidate)).Should().ThrowAsync<InvalidOperationException>();
        await f.Set(MarkdownDisplayMode.Source);
        (await f.Capture()).Should().Be(MarkdownDisplayMode.Source);
        f.Service.Description.Should().Contain("Source (saved)");
        await using var restarted = f.Create();
        await restarted.ObserveAsync(f.Token);
        (await restarted.CaptureAsync(() => true, f.Token)).Should().Be(MarkdownDisplayMode.Source);
        var reset = await f.Review(MarkdownDisplayPreference.Default);
        reset.Description.Should().Contain("Source -> Rendered");
        f.Preferences.Value.Should().Be(MarkdownDisplayMode.Source);
        (await f.Confirm(reset)).Should().BeTrue();
        (await f.Capture()).Should().Be(MarkdownDisplayMode.Rendered);
        f.Preferences.Pending.Should().BeFalse();
        f.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested,
            SecurityAuditOutcome.Succeeded, SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Audit.Events.Should().OnlyContain(item => item.ActionId == "configuration.markdown-display"
            && item.TargetId == "preferences.device-local");
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("host")]
    [InlineData("source")]
    [InlineData("session")]
    [InlineData("revision")]
    [InlineData("replaced")]
    [InlineData("channel")]
    [InlineData("model")]
    [InlineData("voice-origin")]
    [InlineData("cancelled")]
    public async Task StaleForeignOrNonOriginalConfirmationHasNoWrite(string boundary)
    {
        await using var f = new Fixture();
        var candidate = await f.Review(MarkdownDisplayMode.Source);
        if (boundary is "expired") { f.Time.Now = f.Time.Now.AddSeconds(60); }
        if (boundary is "host") { f.Eligible = false; }
        if (boundary is "source") { f.Preferences.Value = MarkdownDisplayMode.Rendered; }
        if (boundary is "session") { f.Store.Authority = f.Store.Authority! with { Generation = new(2) }; }
        if (boundary is "revision") { f.Service.HoldUnavailable(); await f.Service.ObserveAsync(f.Token); }
        if (boundary is "replaced") { await f.Review(MarkdownDisplayMode.Rendered); }
        using var cancellation = new CancellationTokenSource();
        if (boundary is "cancelled") { cancellation.Cancel(); }
        var confirm = () => f.Service.ConfirmAsync(candidate,
            boundary is "channel" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi,
            boundary is "model" ? SecurityAuditInitiator.ModelSuggestion
                : boundary is "voice-origin" or "channel" ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            f.Policy, () => f.Eligible, cancellation.Token);
        await confirm.Should().ThrowAsync<Exception>();
        f.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(CallState.Active)]
    [InlineData(CallState.Unknown)]
    public async Task OriginalCallPolicyRejectsVoiceReviewButNotNativeSave(CallState state)
    {
        await using var f = new Fixture();
        f.Call.State = state;
        await new Func<Task>(async () => await f.Review(MarkdownDisplayMode.Source, RequestOrigin.ActivatedVoice))
            .Should().ThrowAsync<InvalidOperationException>();
        await f.Set(MarkdownDisplayMode.Source);
    }

    [Fact]
    public async Task OriginalVoiceSaveUsesFreshCallRevisionAndAuditDenial()
    {
        await using var f = new Fixture();
        var candidate = await f.Review(MarkdownDisplayMode.Source, RequestOrigin.ActivatedVoice);
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
    [InlineData("readback")]
    [InlineData("terminal")]
    [InlineData("receipt")]
    [InlineData("late-host")]
    [InlineData("late-readback")]
    [InlineData("confirm")]
    [InlineData("confirmed-readback")]
    [InlineData("confirmed-host")]
    public async Task AuditStoreReadbackOrReceiptUncertaintyHoldsNewViewerApplication(string boundary)
    {
        await using var f = new Fixture();
        var candidate = await f.Review(MarkdownDisplayMode.Source);
        f.Preferences.LoadFailure = boundary is "load" ? new InvalidDataException() : null;
        f.Preferences.SaveFailure = boundary is "save" ? new IOException() : null;
        f.Preferences.AfterSave = () => { if (boundary is "readback") { f.Preferences.Value = MarkdownDisplayMode.Rendered; } };
        f.Preferences.ConfirmFailure = boundary is "confirm" ? new IOException() : null;
        f.Preferences.AfterConfirm = () =>
        {
            if (boundary is "confirmed-readback") { f.Preferences.Value = MarkdownDisplayMode.Rendered; }
            if (boundary is "confirmed-host") { f.Eligible = false; }
        };
        f.Audit.BeforeWrite = item =>
        {
            if (item.Outcome == SecurityAuditOutcome.Requested)
            {
                if (boundary is "requested") { throw new IOException(); }
                if (boundary is "audit-drift") { f.Preferences.Value = MarkdownDisplayMode.Rendered; }
                if (boundary is "context") { Kora.Core.Diagnostics.HostActivity.RequireCurrent().Activity!.Stop(); }
            }
            if (boundary is "terminal" && item.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException(); }
        };
        f.Store.FailTerminal = boundary is "receipt";
        f.Store.BeforeCommit = record =>
        {
            if (record.State != HostTaskState.Succeeded) { return; }
            if (boundary is "late-host") { f.Eligible = false; }
            if (boundary is "late-readback") { f.Preferences.Value = MarkdownDisplayMode.Rendered; }
        };
        await new Func<Task>(async () => await f.Confirm(candidate)).Should().ThrowAsync<Exception>();
        f.Service.Description.Should().Contain("unavailable");
        await new Func<Task>(async () => await f.Capture()).Should().ThrowAsync<InvalidOperationException>();
        if (boundary is "load" or "requested" or "audit-drift" or "context") { f.Preferences.Writes.Should().Be(0); }
        else
        {
            f.Preferences.Pending.Should().BeTrue();
            await using var restarted = f.Create();
            await new Func<Task>(() => restarted.ObserveAsync(f.Token)).Should().ThrowAsync<InvalidDataException>();
        }
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("host-before")]
    [InlineData("host-after")]
    [InlineData("unknown")]
    public async Task CaptureNeverAssumesSavedOrHostState(string boundary)
    {
        await using var f = new Fixture();
        await f.Service.ObserveAsync(f.Token);
        if (boundary is "changed") { f.Preferences.Value = MarkdownDisplayMode.Source; }
        if (boundary is "unknown") { f.Preferences.Value = (MarkdownDisplayMode)4; }
        if (boundary is "host-before") { f.Eligible = false; }
        if (boundary is "host-after") { f.Preferences.BeforeLoad = () => f.Eligible = false; }
        await new Func<Task>(async () => await f.Capture()).Should().ThrowAsync<Exception>();
        f.Preferences.Writes.Should().Be(0);
        f.Service.Description.Should().Contain("unavailable");
    }

    [Theory]
    [InlineData(false, "host")]
    [InlineData(false, "cancelled")]
    [InlineData(true, "host")]
    [InlineData(true, "cancelled")]
    [InlineData(true, "revision")]
    public async Task TerminalReceiptCannotReviveLostReadOrReview(bool reviewing, string boundary)
    {
        await using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        f.Store.BeforeCommit = record =>
        {
            if (!record.IsTerminal) { return; }
            if (boundary is "host") { f.Eligible = false; }
            if (boundary is "cancelled") { cancellation.Cancel(); }
            if (boundary is "revision") { f.Service.HoldUnavailable(); }
        };
        Func<Task> operation = async () =>
        {
            if (reviewing) { await f.Service.ReviewAsync(MarkdownDisplayMode.Source, RequestOrigin.LocalUi, f.Policy.Current.Revision,
                f.Policy, () => f.Eligible, cancellation.Token); }
            else { await f.Service.InspectAsync(RequestOrigin.LocalUi, () => f.Eligible, cancellation.Token); }
        };
        await operation.Should().ThrowAsync<Exception>();
        f.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task AsynchronousCaptureIsJoinedOnDisposalAndConcurrentControlsCannotPassIt()
    {
        await using var f = new Fixture();
        await f.Service.ObserveAsync(f.Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        f.Preferences.BeforeLoad = () =>
        {
            entered.SetResult();
            SynchronizationContext.Current.Should().BeNull("the store must not execute on the initiating context");
            release.Wait(f.Token);
        };
        var context = SynchronizationContext.Current;
        Task<MarkdownDisplayMode> pending;
        SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
        try { pending = f.Capture(); }
        finally { SynchronizationContext.SetSynchronizationContext(context); }
        await entered.Task.WaitAsync(f.Token);
        f.Service.Description.Should().Contain("pending");
        await new Func<Task>(() => f.Service.ObserveAsync(f.Token)).Should().ThrowAsync<InvalidOperationException>();
        var disposal = f.Service.DisposeAsync().AsTask();
        disposal.IsCompleted.Should().BeFalse("the synchronous store read must physically finish");
        release.Set();
        await new Func<Task>(async () => await pending).Should().ThrowAsync<OperationCanceledException>();
        await disposal;
        await f.Service.DisposeAsync();
        await new Func<Task>(async () => await f.Capture()).Should().ThrowAsync<ObjectDisposedException>();
        f.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task CaptureCannotReturnModeWhenClosureBeginsAfterItsLastCancellationCheck()
    {
        await using var f = new Fixture();
        await f.Service.ObserveAsync(f.Token);
        var checks = 0;
        Task? disposal = null;
        var capture = () => f.Service.CaptureAsync(() =>
        {
            if (++checks == 2) { disposal = f.Service.DisposeAsync().AsTask(); }
            return true;
        }, f.Token);
        await capture.Should().ThrowAsync<OperationCanceledException>();
        disposal.Should().NotBeNull();
        await disposal!;
        checks.Should().Be(2);
        f.Preferences.Writes.Should().Be(0);
        f.Service.Description.Should().Contain("unavailable");
    }

    [Fact]
    public async Task FailedObservationReportsUnavailableThroughEnabledTypedDiagnostics()
    {
        await using var f = new Fixture();
        var logger = new Kora.Application.UnitTests.Network.EnabledLogger<MarkdownDisplayConfigurationService>();
        await using var service = new MarkdownDisplayConfigurationService(f.Preferences, f.Admission, f.Audit, f.Time, logger);
        f.Preferences.LoadFailure = new InvalidDataException("Malformed saved preference");
        await new Func<Task>(() => service.ObserveAsync(f.Token)).Should().ThrowAsync<InvalidDataException>();
        service.Description.Should().Contain("unavailable");
        logger.Messages.Should().ContainSingle().Which.Should().Contain("no saved default was applied");
        f.Preferences.Writes.Should().Be(0);
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        internal Fixture() { Admission = new(Store, Store, new(Store)); Policy = new(Call); Service = Create(); }
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        internal bool Eligible { get; set; } = true;
        internal Preferences Preferences { get; } = new();
        internal ConversationalReplyConfigurationServiceTests.Audit Audit { get; } = new();
        internal ConversationalReplyConfigurationServiceTests.Call Call { get; } = new();
        internal AudioControlTestStore Store { get; } = new();
        internal DetailPresentationConfigurationServiceTests.Clock Time { get; } = new();
        internal AudioControlAdmission Admission { get; }
        internal CallCommunicationPolicy Policy { get; }
        internal MarkdownDisplayConfigurationService Service { get; }
        internal MarkdownDisplayConfigurationService Create() => new(Preferences, Admission, Audit, Time,
            NullLogger<MarkdownDisplayConfigurationService>.Instance);
        internal Task<MarkdownDisplayMode> Capture() => Service.CaptureAsync(() => Eligible, Token);
        internal Task<MarkdownDisplayReview> Review(MarkdownDisplayMode value, RequestOrigin origin = RequestOrigin.LocalUi) =>
            Service.ReviewAsync(value, origin, Policy.Current.Revision, Policy, () => Eligible, Token);
        internal Task<bool> Confirm(MarkdownDisplayReview review, RequestOrigin origin = RequestOrigin.LocalUi,
            SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser) =>
            Service.ConfirmAsync(review, origin, initiator, Policy, () => Eligible, Token);
        internal async Task Set(MarkdownDisplayMode value) => (await Confirm(await Review(value))).Should().BeTrue();
        public async ValueTask DisposeAsync() { await Service.DisposeAsync(); Policy.Dispose(); await Admission.DisposeAsync(); }
    }

    internal sealed class Preferences : IMarkdownDisplayPreferences
    {
        internal MarkdownDisplayMode? Value { get; set; }
        internal bool Pending { get; private set; }
        internal int Writes { get; private set; }
        internal Exception? LoadFailure { get; set; }
        internal Exception? SaveFailure { get; set; }
        internal Exception? ConfirmFailure { get; set; }
        internal Action? BeforeLoad { get; set; }
        internal Action? AfterSave { get; set; }
        internal Action? AfterConfirm { get; set; }
        public MarkdownDisplayMode? Load()
        {
            BeforeLoad?.Invoke();
            return LoadFailure is { } failure ? throw failure : Pending ? throw new InvalidDataException() : Value;
        }
        public MarkdownDisplayMode? ReadBack() => Value;
        public void BeginWrite() => Pending = true;
        public void Save(MarkdownDisplayMode value)
        {
            if (SaveFailure is { } failure) { throw failure; }
            Value = value; Writes++; AfterSave?.Invoke();
        }
        public void ConfirmWrite()
        {
            if (ConfirmFailure is { } failure) { throw failure; }
            Pending = false; AfterConfirm?.Invoke();
        }
    }
}
