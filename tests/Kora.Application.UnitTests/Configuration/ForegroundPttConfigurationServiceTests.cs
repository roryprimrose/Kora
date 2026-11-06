using System.Diagnostics;

using AwesomeAssertions;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class ForegroundPttConfigurationServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public ForegroundPttConfigurationServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser)]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand)]
    [InlineData(RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand)]
    public async Task OriginalRoutesSaveReloadAndClearWithContentFreeAudit(RequestOrigin origin, SecurityAuditInitiator initiator)
    {
        await using var f = new Fixture();
        f.Service.Get().Should().BeNull();
        var changes = 0;
        f.Service.Changed += (_, _) => changes++;
        f.Service.Observe();
        f.Service.Get()!.Shortcut.Should().Be(ForegroundPttShortcut.Unassigned);
        await f.Set(ForegroundPttShortcut.ControlShiftSpace, origin, initiator);
        f.Service.Get()!.Shortcut.Should().Be(ForegroundPttShortcut.ControlShiftSpace);
        var restarted = new ForegroundPttConfigurationService(f.Preferences, f.Admission, f.Audit);
        restarted.Observe();
        restarted.Get()!.Shortcut.Should().Be(ForegroundPttShortcut.ControlShiftSpace);
        await f.Set(ForegroundPttShortcut.Unassigned);
        f.Service.Get()!.Shortcut.Should().Be(ForegroundPttShortcut.Unassigned);
        changes.Should().BeGreaterThan(2);
        f.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested,
            SecurityAuditOutcome.Succeeded, SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Audit.Events.Should().OnlyContain(item => item.ActionId == "configuration.foreground-ptt-shortcut");
        f.Store.Pending.Should().BeFalse();
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
        if (boundary is "saved") { f.Store.Value = "Control Shift F9"; }
        if (boundary is "host") { f.Eligible = false; }
        using var cancellation = new CancellationTokenSource();
        if (boundary is "cancel") { await cancellation.CancelAsync(); }
        var apply = () => f.Service.SetAsync(ForegroundPttShortcut.ControlShiftSpace, revision, f.Policy.Current.Revision,
            RequestOrigin.LocalUi, boundary is "model" ? SecurityAuditInitiator.ModelSuggestion
                : boundary is "voice-origin" ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            f.Policy, () => f.Eligible, cancellation.Token);
        await apply.Should().ThrowAsync<Exception>();
        f.Store.Writes.Should().Be(0);
    }

    [Fact]
    public async Task ProtectedCallsDenyVoiceWithoutRelabellingNativeInput()
    {
        await using var f = new Fixture();
        f.Service.Observe();
        f.Call.State = CallState.Active;
        (await f.Set(ForegroundPttShortcut.ControlShiftSpace, RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand))
            .Should().BeFalse();
        f.Store.Writes.Should().Be(0);
        f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
        await f.Set(ForegroundPttShortcut.ControlShiftF9);
        f.Service.Get()!.Shortcut.Should().Be(ForegroundPttShortcut.ControlShiftF9);
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
        await f.Admission.RunAsync(RequestOrigin.LocalUi, static () => true, (_, _) => true, TestContext.Current.CancellationToken);
        f.Store.LoadFailure = boundary is "load" ? new InvalidDataException() : null;
        f.Store.SaveFailure = boundary switch { "save" => new IOException(), "access" => new UnauthorizedAccessException(), _ => null };
        f.Store.AfterSave = () => { if (boundary is "readback") { f.Store.Value = "Unassigned"; } };
        f.Audit.BeforeWrite = item =>
        {
            if (boundary is "requested" && item.Outcome == SecurityAuditOutcome.Requested
                || boundary is "terminal" && item.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException(); }
        };
        f.Tasks.FailTerminal = boundary is "receipt";
        f.Tasks.BeforeCommit = task =>
        {
            if (task.State != HostTaskState.Succeeded) { return; }
            if (boundary is "late-readback") { f.Store.Value = "Unassigned"; }
            if (boundary is "late-host") { f.Eligible = false; }
        };
        f.Store.ConfirmFailure = boundary is "confirm" ? new IOException() : null;
        f.Store.AfterConfirm = () =>
        {
            if (boundary is "confirmed-readback") { f.Store.Value = "Unassigned"; }
            if (boundary is "confirmed-load") { f.Store.LoadFailure = new IOException(); }
        };
        await FluentActions.Awaiting(() => f.Set(ForegroundPttShortcut.ControlShiftSpace)).Should().ThrowAsync<Exception>();
        f.Service.Get().Should().BeNull();
        f.Store.LoadFailure = new InvalidDataException();
        f.Service.Invoking(service => service.Observe()).Should().Throw<InvalidDataException>();
        f.Service.Get().Should().BeNull();
    }

    [Fact]
    public async Task ReentrantWritesAndObservationCannotReplaceCommittingPreference()
    {
        await using var f = new Fixture();
        f.Service.Observe();
        f.Audit.BeforeWrite = item =>
        {
            if (item.Outcome != SecurityAuditOutcome.Requested) { return; }
            f.Service.Invoking(service => service.Observe()).Should().Throw<InvalidOperationException>();
        };
        await f.Set(ForegroundPttShortcut.ControlShiftF9);
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        public Fixture()
        {
            Admission = new(Tasks, Tasks, new(Tasks));
            Preferences = new(Store);
            Service = new(Preferences, Admission, Audit);
            Policy = new(Call);
        }
        internal bool Eligible { get; set; } = true;
        internal PreferenceStore Store { get; } = new();
        internal LocalForegroundPttPreferences Preferences { get; }
        internal AudioControlTestStore Tasks { get; } = new();
        internal ConversationalReplyConfigurationServiceTests.Audit Audit { get; } = new();
        internal ConversationalReplyConfigurationServiceTests.Call Call { get; } = new();
        internal AudioControlAdmission Admission { get; }
        internal CallCommunicationPolicy Policy { get; }
        internal ForegroundPttConfigurationService Service { get; }
        internal Task<bool> Set(ForegroundPttShortcut shortcut, RequestOrigin origin = RequestOrigin.LocalUi,
            SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser) =>
            Service.SetAsync(shortcut, Service.Get()!.Revision, Policy.Current.Revision, origin, initiator,
                Policy, () => Eligible, TestContext.Current.CancellationToken);
        public async ValueTask DisposeAsync() { Policy.Dispose(); await Admission.DisposeAsync(); }
    }

    internal sealed class PreferenceStore : IPreferenceStore
    {
        internal string? Value { get; set; }
        internal bool Pending { get; private set; }
        internal int Writes { get; private set; }
        internal Exception? SaveFailure { get; set; }
        internal Exception? LoadFailure { get; set; }
        internal Exception? ConfirmFailure { get; set; }
        internal Action? AfterSave { get; set; }
        internal Action? AfterConfirm { get; set; }
        public string? ReadText(string fileName) => fileName.Contains("unconfirmed", StringComparison.Ordinal)
            ? LoadFailure is { } failure ? throw failure : Pending ? "1" : null : Value;
        public void WriteText(string fileName, string contents)
        {
            if (fileName.Contains("unconfirmed", StringComparison.Ordinal)) { Pending = true; return; }
            if (SaveFailure is { } failure) { throw failure; }
            Value = contents; Writes++; AfterSave?.Invoke();
        }
        public void Delete(string fileName)
        {
            if (ConfirmFailure is { } failure) { throw failure; }
            Pending = false; AfterConfirm?.Invoke();
        }
        public string[]? ReadLines(string fileName) => throw new NotSupportedException();
        public void WriteLines(string fileName, IEnumerable<string> contents) => throw new NotSupportedException();
    }
}
