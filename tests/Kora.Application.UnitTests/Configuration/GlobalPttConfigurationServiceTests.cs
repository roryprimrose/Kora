using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class GlobalPttConfigurationServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public GlobalPttConfigurationServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public void DeviceLocalPreferenceRoundTripsAndUnconfirmedWritesRemainUnavailable()
    {
        var root = Path.Combine(Environment.CurrentDirectory, ".scratch", "global-ptt-tests", Guid.NewGuid().ToString("N"));
        var paths = new Paths(root, root);
        try
        {
            var preference = new LocalGlobalPttPreferences(paths);
            preference.Load().Should().Be(GlobalPttShortcut.Unassigned);
            preference.BeginWrite();
            preference.Save(GlobalPttShortcut.ControlShiftF9);
            new LocalGlobalPttPreferences(paths).Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
            preference.ReadBack().Should().Be(GlobalPttShortcut.ControlShiftF9);
            preference.ConfirmWrite();
            new LocalGlobalPttPreferences(paths).Load().Should().Be(GlobalPttShortcut.ControlShiftF9);
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
    }

    private sealed record Paths(string LocalRoot, string RoamingRoot) : IApplicationDataPaths;

    [Theory]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser)]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand)]
    [InlineData(RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand)]
    public async Task OriginalRoutesSaveResetAndReloadWithAuditedReadback(RequestOrigin origin, SecurityAuditInitiator initiator)
    {
        await using var f = new Fixture();
        f.Service.Get().Should().BeNull();
        f.Service.Observe();
        f.Service.Get()!.Shortcut.Should().Be(GlobalPttShortcut.Unassigned);
        await f.Set(GlobalPttShortcut.ControlShiftSpace, origin, initiator);
        var restarted = new GlobalPttConfigurationService(f.Preferences, f.Inner.Admission, f.Inner.Audit);
        restarted.Observe();
        restarted.Get()!.Shortcut.Should().Be(GlobalPttShortcut.ControlShiftSpace);
        await f.Set(GlobalPttShortcut.Unassigned);
        f.Service.Get()!.Shortcut.Should().Be(GlobalPttShortcut.Unassigned);
        f.Inner.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested,
            SecurityAuditOutcome.Succeeded, SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Inner.Audit.Events.Should().OnlyContain(item => item.ActionId == "configuration.global-ptt-shortcut");
        f.Inner.Store.Pending.Should().BeFalse();
    }

    [Theory]
    [InlineData("unobserved")]
    [InlineData("stale")]
    [InlineData("saved")]
    [InlineData("host")]
    [InlineData("model")]
    [InlineData("voice-origin")]
    [InlineData("cancel")]
    public async Task InvalidOrStaleAdmissionWritesNoPreference(string boundary)
    {
        await using var f = new Fixture();
        f.Service.Observe();
        var revision = f.Service.Get()!.Revision;
        if (boundary is "unobserved") { f.Service.HoldUnavailable(); }
        if (boundary is "stale") { f.Service.Observe(); }
        if (boundary is "saved") { f.Inner.Store.Value = "Control Shift F9"; }
        if (boundary is "host") { f.Inner.Eligible = false; }
        using var cancellation = new CancellationTokenSource();
        if (boundary is "cancel") { await cancellation.CancelAsync(); }
        await FluentActions.Awaiting(() => f.Service.SetAsync(GlobalPttShortcut.ControlShiftSpace, revision,
            f.Inner.Policy.Current.Revision, RequestOrigin.LocalUi,
            boundary is "model" ? SecurityAuditInitiator.ModelSuggestion
                : boundary is "voice-origin" ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            f.Inner.Policy, () => f.Inner.Eligible, cancellation.Token)).Should().ThrowAsync<Exception>();
        f.Inner.Store.Writes.Should().Be(0);
    }

    [Fact]
    public async Task ProtectedCallsDenyVoiceButRetainOriginalNativeAdmission()
    {
        await using var f = new Fixture();
        f.Service.Observe();
        f.Inner.Call.State = CallState.Active;
        (await f.Set(GlobalPttShortcut.ControlShiftSpace, RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand))
            .Should().BeFalse();
        f.Inner.Store.Writes.Should().Be(0);
        f.Inner.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
        await f.Set(GlobalPttShortcut.ControlShiftF9);
        f.Service.Get()!.Shortcut.Should().Be(GlobalPttShortcut.ControlShiftF9);
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
    public async Task UnconfirmedStorageAuditAndAdmissionNeverPublishBinding(string boundary)
    {
        await using var f = new Fixture();
        f.Service.Observe();
        await f.Inner.Admission.RunAsync(RequestOrigin.LocalUi, static () => true, (_, _) => true, TestContext.Current.CancellationToken);
        f.Inner.Store.LoadFailure = boundary is "load" ? new InvalidDataException() : null;
        f.Inner.Store.SaveFailure = boundary switch { "save" => new IOException(), "access" => new UnauthorizedAccessException(), _ => null };
        f.Inner.Store.AfterSave = () => { if (boundary is "readback") { f.Inner.Store.Value = "Unassigned"; } };
        f.Inner.Audit.BeforeWrite = item =>
        {
            if (boundary is "requested" && item.Outcome == SecurityAuditOutcome.Requested
                || boundary is "terminal" && item.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException(); }
        };
        f.Inner.Tasks.FailTerminal = boundary is "receipt";
        f.Inner.Tasks.BeforeCommit = task =>
        {
            if (task.State != HostTaskState.Succeeded) { return; }
            if (boundary is "late-readback") { f.Inner.Store.Value = "Unassigned"; }
            if (boundary is "late-host") { f.Inner.Eligible = false; }
        };
        f.Inner.Store.ConfirmFailure = boundary is "confirm" ? new IOException() : null;
        f.Inner.Store.AfterConfirm = () =>
        {
            if (boundary is "confirmed-readback") { f.Inner.Store.Value = "Unassigned"; }
            if (boundary is "confirmed-load") { f.Inner.Store.LoadFailure = new IOException(); }
        };
        await FluentActions.Awaiting(() => f.Set(GlobalPttShortcut.ControlShiftSpace)).Should().ThrowAsync<Exception>();
        f.Service.Get().Should().BeNull();
        f.Inner.Store.LoadFailure = new InvalidDataException();
        f.Service.Invoking(service => service.Observe()).Should().Throw<InvalidDataException>();
        f.Service.Get().Should().BeNull();
    }

    [Fact]
    public async Task CommittingPreferenceCannotBeReobservedAndPendingWritesFailClosed()
    {
        await using var f = new Fixture();
        f.Service.Observe();
        f.Inner.Audit.BeforeWrite = item =>
        {
            if (item.Outcome == SecurityAuditOutcome.Requested)
            { f.Service.Invoking(service => service.Observe()).Should().Throw<InvalidOperationException>(); }
        };
        await f.Set(GlobalPttShortcut.ControlShiftF9);
        f.Preferences.BeginWrite();
        f.Preferences.Invoking(preferences => preferences.Load()).Should().Throw<InvalidDataException>();
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        internal ForegroundPttConfigurationServiceTests.Fixture Inner { get; } = new();
        internal LocalGlobalPttPreferences Preferences { get; }
        internal GlobalPttConfigurationService Service { get; }
        internal Fixture()
        {
            Preferences = new(Inner.Store);
            Service = new(Preferences, Inner.Admission, Inner.Audit);
        }
        internal Task<bool> Set(GlobalPttShortcut shortcut, RequestOrigin origin = RequestOrigin.LocalUi,
            SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser) =>
            Service.SetAsync(shortcut, Service.Get()!.Revision, Inner.Policy.Current.Revision, origin, initiator,
                Inner.Policy, () => Inner.Eligible, TestContext.Current.CancellationToken);
        public ValueTask DisposeAsync() => Inner.DisposeAsync();
    }
}
