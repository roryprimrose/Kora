using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class VoiceCaptureTimingConfigurationServiceTests : IDisposable
{
    private readonly ConversationalReplyConfigurationServiceTests tracing = new();
    public void Dispose() => tracing.Dispose();
    internal static VoiceCaptureTimingConfigurationService Create(Preferences preferences, ConversationalReplyConfigurationServiceTests.Fixture host) =>
        new(preferences, host.Admission, host.Audit, NullLogger<VoiceCaptureTimingConfigurationService>.Instance);

    [Fact]
    public async Task ConfirmedTimingPersistsReloadsAndUsesTypedAuditWithoutAudioEffects()
    {
        await using var host = new ConversationalReplyConfigurationServiceTests.Fixture();
        var preferences = new Preferences();
        var service = Create(preferences, host);
        service.Get().IsSaved.Should().BeFalse();
        service.GetTiming().Should().Be(VoiceCaptureTiming.Default);
        var proposed = new VoiceCaptureTiming(7, 20, 2);
        (await Set(service, host, proposed)).Should().BeTrue();
        service.GetTiming().Should().Be(proposed);
        service.Get().IsSaved.Should().BeTrue();
        Create(preferences, host).GetTiming().Should().Be(proposed);
        preferences.Pending.Should().BeFalse();
        host.Audit.Events.Where(item => string.Equals(item.ActionId, "configuration.voice-capture-timing", StringComparison.Ordinal))
            .Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("host")]
    [InlineData("cancel")]
    [InlineData("model")]
    [InlineData("voice-origin")]
    public async Task InvalidAdmissionHasNoWrite(string boundary)
    {
        await using var host = new ConversationalReplyConfigurationServiceTests.Fixture();
        var preferences = new Preferences();
        var service = Create(preferences, host);
        var revision = service.Get().Revision;
        if (boundary is "stale") { service.Reload(); }
        if (boundary is "host") { host.Eligible = false; }
        using var cancellation = new CancellationTokenSource();
        if (boundary is "cancel") { cancellation.Cancel(); }
        var apply = () => service.SetAsync(new(7, 20, 2), revision, host.Policy.Current.Revision, RequestOrigin.LocalUi,
            boundary is "model" ? SecurityAuditInitiator.ModelSuggestion : boundary is "voice-origin" ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            host.Policy, () => host.Eligible, cancellation.Token);
        await apply.Should().ThrowAsync<Exception>();
        preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task ProtectedCallDeniesVoiceWithoutLaunderingAndAllowsSeparateNativeRequest()
    {
        await using var host = new ConversationalReplyConfigurationServiceTests.Fixture();
        var preferences = new Preferences();
        var service = Create(preferences, host);
        host.Call.State = Kora.Core.Communication.CallState.Active;
        (await Set(service, host, new(7, 20, 2), RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand)).Should().BeFalse();
        preferences.Writes.Should().Be(0);
        service.GetTiming().Should().Be(VoiceCaptureTiming.Default);
        host.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
        (await Set(service, host, new(7, 20, 2))).Should().BeTrue();
    }

    [Theory]
    [InlineData("load")]
    [InlineData("save")]
    [InlineData("readback")]
    [InlineData("audit")]
    [InlineData("receipt")]
    [InlineData("owner")]
    [InlineData("confirm")]
    public async Task FailedOrUncertainWritesCannotSupplyCaptureTiming(string boundary)
    {
        await using var host = new ConversationalReplyConfigurationServiceTests.Fixture();
        var preferences = new Preferences();
        var service = Create(preferences, host);
        await host.Admission.RunAsync(RequestOrigin.LocalUi, static () => true, (_, _) => true, TestContext.Current.CancellationToken);
        preferences.LoadFailure = boundary is "load" ? new InvalidDataException() : null;
        preferences.SaveFailure = boundary is "save" ? new IOException() : null;
        preferences.ConfirmFailure = boundary is "confirm" ? new IOException() : null;
        preferences.AfterSave = () => { if (boundary is "readback") { preferences.Value = VoiceCaptureTiming.Default; } };
        host.Audit.BeforeWrite = item =>
        {
            if (boundary is "audit" && string.Equals(item.ActionId, "configuration.voice-capture-timing", StringComparison.Ordinal) && item.Outcome == SecurityAuditOutcome.Succeeded)
            { throw new IOException(); }
        };
        host.Store.FailTerminal = boundary is "receipt";
        host.Store.BeforeCommit = task => { if (boundary is "owner" && task.State == HostTaskState.Succeeded) { host.Eligible = false; } };
        await FluentActions.Awaiting(() => Set(service, host, new(7, 20, 2))).Should().ThrowAsync<Exception>();
        service.Invoking(item => item.GetTiming()).Should().Throw<InvalidOperationException>();
        var restarted = Create(preferences, host);
        restarted.Get().Timing.Should().BeNull();
        restarted.Invoking(item => item.GetTiming()).Should().Throw<InvalidOperationException>();
    }

    private static Task<bool> Set(VoiceCaptureTimingConfigurationService service, ConversationalReplyConfigurationServiceTests.Fixture host,
        VoiceCaptureTiming proposed, RequestOrigin origin = RequestOrigin.LocalUi, SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser) =>
        service.SetAsync(proposed, service.Get().Revision, host.Policy.Current.Revision, origin, initiator,
            host.Policy, () => host.Eligible, TestContext.Current.CancellationToken);

    [Fact]
    public async Task ReloadAndConcurrentWritesCannotReplaceAnInProgressConfirmedCommit()
    {
        await using var host = new ConversationalReplyConfigurationServiceTests.Fixture();
        var preferences = new Preferences();
        var service = Create(preferences, host);
        await host.Admission.RunAsync(RequestOrigin.LocalUi, static () => true, (_, _) => true, TestContext.Current.CancellationToken);
        Task<bool>? competing = null;
        host.Audit.BeforeWrite = item =>
        {
            if (!string.Equals(item.ActionId, "configuration.voice-capture-timing", StringComparison.Ordinal) || item.Outcome != SecurityAuditOutcome.Requested) { return; }
            service.Invoking(value => value.Reload()).Should().Throw<InvalidOperationException>();
            competing = Set(service, host, new(8, 30, 3));
        };
        (await Set(service, host, new(7, 20, 2))).Should().BeTrue();
        await FluentActions.Awaiting(() => competing!).Should().ThrowAsync<InvalidOperationException>();
        service.GetTiming().Should().Be(new VoiceCaptureTiming(7, 20, 2));
        preferences.Writes.Should().Be(1);
    }

    [Theory]
    [InlineData("storage-drift")]
    [InlineData("invalid-domain")]
    [InlineData("unavailable")]
    [InlineData("origin")]
    public async Task UnknownOrExternallyChangedStateRefusesNewWritesAndCapture(string boundary)
    {
        await using var host = new ConversationalReplyConfigurationServiceTests.Fixture();
        var preferences = new Preferences();
        var service = Create(preferences, host);
        if (boundary is "storage-drift") { preferences.Value = new(7, 20, 2); }
        if (boundary is "invalid-domain") { preferences.Value = new(1, 15, 1); service.Reload(); }
        if (boundary is "unavailable") { preferences.LoadFailure = new IOException(); service.Reload(); }
        var apply = () => Set(service, host, new(7, 20, 2), boundary is "origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        await apply.Should().ThrowAsync<Exception>();
        preferences.Writes.Should().Be(0);
        if (boundary is not "origin") { service.Invoking(value => value.GetTiming()).Should().Throw<InvalidOperationException>(); }
    }

    [Theory]
    [InlineData("late-readback")]
    [InlineData("confirmed-readback")]
    [InlineData("confirmed-load")]
    [InlineData("late-call")]
    [InlineData("late-cancel")]
    [InlineData("requested-audit")]
    public async Task PostWriteBoundariesRetainUnavailableStateRatherThanPublishingSuccess(string boundary)
    {
        await using var host = new ConversationalReplyConfigurationServiceTests.Fixture();
        var preferences = new Preferences();
        var service = Create(preferences, host);
        await host.Admission.RunAsync(RequestOrigin.LocalUi, static () => true, (_, _) => true, TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        host.Store.BeforeCommit = task =>
        {
            if (task.State != HostTaskState.Succeeded) { return; }
            if (boundary is "late-readback") { preferences.Value = VoiceCaptureTiming.Default; }
            if (boundary is "late-call") { host.Call.State = Kora.Core.Communication.CallState.Active; }
            if (boundary is "late-cancel") { cancellation.Cancel(); }
        };
        preferences.AfterConfirm = () =>
        {
            if (boundary is "confirmed-readback") { preferences.Value = VoiceCaptureTiming.Default; }
            if (boundary is "confirmed-load") { preferences.LoadFailure = new IOException(); }
        };
        host.Audit.BeforeWrite = item =>
        {
            if (boundary is "requested-audit" && item.Outcome == SecurityAuditOutcome.Requested) { throw new IOException(); }
        };
        var apply = () => service.SetAsync(new(7, 20, 2), service.Get().Revision, host.Policy.Current.Revision, RequestOrigin.LocalUi,
            SecurityAuditInitiator.TypedCommand, host.Policy, static () => true, cancellation.Token);
        await apply.Should().ThrowAsync<Exception>();
        service.Invoking(value => value.GetTiming()).Should().Throw<InvalidOperationException>();
        if (boundary is not "requested-audit") { preferences.Pending.Should().BeTrue(); }
    }

    internal sealed class Preferences : IVoiceCaptureTimingPreferences
    {
        public VoiceCaptureTiming? Value { get; set; }
        public bool Pending { get; private set; }
        public int Writes { get; private set; }
        public Exception? LoadFailure { get; set; }
        public Exception? SaveFailure { get; set; }
        public Exception? ConfirmFailure { get; set; }
        public Action? AfterSave { get; set; }
        public Action? AfterConfirm { get; set; }
        public VoiceCaptureTiming? Load() => LoadFailure is { } failure ? throw failure : Pending ? throw new InvalidDataException() : Value;
        public VoiceCaptureTiming? ReadBack() => Value;
        public void BeginWrite() => Pending = true;
        public void Save(VoiceCaptureTiming timing) { if (SaveFailure is { } failure) { throw failure; } Value = timing; Writes++; AfterSave?.Invoke(); }
        public void ConfirmWrite() { if (ConfirmFailure is { } failure) { throw failure; } Pending = false; AfterConfirm?.Invoke(); }
    }
}
