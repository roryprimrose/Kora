using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Hosting;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class SpokenSendReplyConfigurationServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public SpokenSendReplyConfigurationServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public void AtomicDevicePreferenceSurvivesRestartOnlyAfterConfirmation()
    {
        var root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts", "spoken-send-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new Paths(root);
            var preferences = new LocalSpokenSendReplyPreferences(paths);
            preferences.Load().Should().BeNull();
            preferences.BeginWrite();
            preferences.Save(true);
            new LocalSpokenSendReplyPreferences(paths).Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
            preferences.ConfirmWrite();
            new LocalSpokenSendReplyPreferences(paths).Load().Should().BeTrue();
            preferences.BeginWrite();
            preferences.Save(false);
            preferences.ConfirmWrite();
            new LocalSpokenSendReplyPreferences(paths).Load().Should().BeFalse();
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
    }

    private sealed record Paths(string LocalRoot) : IApplicationDataPaths
    {
        public string RoamingRoot => LocalRoot;
    }

    [Fact]
    public async Task DefaultOffOptInRestartAndExplicitOffUseAuditedAtomicPreferences()
    {
        await using var f = new Fixture();
        f.Service.Get().Should().BeNull();
        var changes = 0;
        f.Service.Changed += (_, _) => changes++;
        f.Service.Observe();
        f.Service.Get()!.Enabled.Should().BeFalse();
        await f.Set(true);
        f.Service.Get()!.Enabled.Should().BeTrue();
        f.Store.Pending.Should().BeFalse();
        var restarted = new SpokenSendReplyConfigurationService(new(f.Store), f.Admission, f.Audit);
        restarted.Observe();
        restarted.Get()!.Enabled.Should().BeTrue();
        await f.Set(false);
        f.Store.Value.Should().Be("Off");
        f.Service.Get()!.Enabled.Should().BeFalse();
        f.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested,
            SecurityAuditOutcome.Succeeded, SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Audit.Events.Should().OnlyContain(item => item.ActionId == "configuration.spoken-send-replies");
        changes.Should().BeGreaterThan(2);
    }

    [Theory]
    [InlineData("bad")]
    [InlineData("")]
    [InlineData("true")]
    [InlineData("pending")]
    public async Task MalformedAndUnconfirmedSavedValuesFailClosed(string saved)
    {
        await using var f = new Fixture();
        f.Store.Value = saved;
        if (saved is "pending") { new LocalSpokenSendReplyPreferences(f.Store).BeginWrite(); }
        FluentActions.Invoking(f.Service.Observe).Should().Throw<InvalidDataException>();
        f.Service.Get().Should().BeNull();
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("saved")]
    [InlineData("host")]
    [InlineData("model")]
    [InlineData("voice-origin")]
    public async Task StaleOrNonoriginalChangesCannotWrite(string boundary)
    {
        await using var f = new Fixture();
        f.Service.Observe();
        var revision = f.Service.Get()!.Revision;
        if (boundary is "stale") { f.Service.Observe(); }
        if (boundary is "saved") { f.Store.Value = "On"; }
        if (boundary is "host") { f.Eligible = false; }
        await FluentActions.Awaiting(() => f.Service.SetAsync(true, revision, f.Policy.Current.Revision, RequestOrigin.LocalUi,
            boundary is "model" ? SecurityAuditInitiator.ModelSuggestion : boundary is "voice-origin"
                ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            f.Policy, () => f.Eligible, TestContext.Current.CancellationToken)).Should().ThrowAsync<Exception>();
        f.Store.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("load")]
    [InlineData("save")]
    [InlineData("access")]
    [InlineData("readback")]
    [InlineData("requested")]
    [InlineData("terminal")]
    [InlineData("receipt")]
    [InlineData("confirm")]
    [InlineData("confirmed-readback")]
    [InlineData("confirmed-load")]
    [InlineData("late-host")]
    [InlineData("late-readback")]
    [InlineData("late-cancel")]
    [InlineData("confirm-host")]
    public async Task PersistenceAuditOrFinalAdmissionFailureCannotEnableReplies(string boundary)
    {
        await using var f = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        f.Service.Observe();
        await f.Admission.RunAsync(RequestOrigin.LocalUi, static () => true, (_, _) => true, TestContext.Current.CancellationToken);
        f.Store.LoadFailure = boundary is "load" ? new InvalidDataException() : null;
        f.Store.SaveFailure = boundary switch
        {
            "save" => new IOException(),
            "access" => new UnauthorizedAccessException(),
            _ => null,
        };
        f.Store.AfterSave = () => { if (boundary is "readback") { f.Store.Value = "Off"; } };
        f.Store.ConfirmFailure = boundary is "confirm" ? new IOException() : null;
        f.Store.AfterConfirm = () =>
        {
            if (boundary is "confirmed-readback") { f.Store.Value = "Off"; }
            if (boundary is "confirmed-load") { f.Store.LoadFailure = new IOException(); }
            if (boundary is "confirm-host") { f.Eligible = false; }
        };
        f.Audit.BeforeWrite = item =>
        {
            if (boundary is "requested" && item.Outcome == SecurityAuditOutcome.Requested
                || boundary is "terminal" && item.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException(); }
        };
        f.Tasks.FailTerminal = boundary is "receipt";
        f.Tasks.BeforeCommit = task =>
        {
            if (task.State != HostTaskState.Succeeded) { return; }
            if (boundary is "late-host") { f.Eligible = false; }
            if (boundary is "late-readback") { f.Store.Value = "Off"; }
            if (boundary is "late-cancel") { cancellation.Cancel(); }
        };
        await FluentActions.Awaiting(() => f.Service.SetAsync(true, f.Service.Get()!.Revision, f.Policy.Current.Revision,
            RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser, f.Policy, () => f.Eligible, cancellation.Token))
            .Should().ThrowAsync<Exception>();
        f.Service.Get().Should().BeNull();
    }

    [Fact]
    public async Task ProtectedCallsDenyVoiceButAllowOriginalNativeChoiceAndConcurrentObserveIsRefused()
    {
        await using var f = new Fixture();
        f.Service.Observe();
        f.Call.State = CallState.Active;
        await f.Set(true, RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand);
        f.Store.Writes.Should().Be(0);
        f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
        f.Audit.BeforeWrite = item =>
        {
            if (item.Outcome == SecurityAuditOutcome.Requested)
            { FluentActions.Invoking(f.Service.Observe).Should().Throw<InvalidOperationException>(); }
        };
        await f.Set(true);
        f.Service.Get()!.Enabled.Should().BeTrue();
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        internal Fixture()
        {
            Admission = new(Tasks, Tasks, new(Tasks));
            Policy = new(Call);
            Service = new(new(Store), Admission, Audit);
        }
        internal bool Eligible { get; set; } = true;
        internal ForegroundPttConfigurationServiceTests.PreferenceStore Store { get; } = new();
        internal AudioControlTestStore Tasks { get; } = new();
        internal ConversationalReplyConfigurationServiceTests.Audit Audit { get; } = new();
        internal ConversationalReplyConfigurationServiceTests.Call Call { get; } = new();
        internal AudioControlAdmission Admission { get; }
        internal CallCommunicationPolicy Policy { get; }
        internal SpokenSendReplyConfigurationService Service { get; }
        internal Task<bool> Set(bool enabled, RequestOrigin origin = RequestOrigin.LocalUi,
            SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser) =>
            Service.SetAsync(enabled, Service.Get()!.Revision, Policy.Current.Revision, origin, initiator,
                Policy, () => Eligible, TestContext.Current.CancellationToken);
        public async ValueTask DisposeAsync() { Policy.Dispose(); await Admission.DisposeAsync(); }
    }
}
