using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Hosting;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class RoutingAdviceConfigurationServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public RoutingAdviceConfigurationServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public void AtomicPreferenceRequiresConfirmationAndSurvivesRestart()
    {
        var root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts", "routing-advice-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new Paths(root);
            var preferences = new LocalRoutingAdvicePreferences(paths);
            preferences.Load().Should().BeNull();
            preferences.BeginWrite();
            preferences.Save(true);
            new LocalRoutingAdvicePreferences(paths).Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
            preferences.ConfirmWrite();
            new LocalRoutingAdvicePreferences(paths).Load().Should().BeTrue();
            preferences.BeginWrite();
            preferences.Save(false);
            preferences.ConfirmWrite();
            new LocalRoutingAdvicePreferences(paths).Load().Should().BeFalse();
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
    }

    private sealed record Paths(string LocalRoot) : IApplicationDataPaths
    {
        public string RoamingRoot => LocalRoot;
    }

    [Fact]
    public async Task DefaultOffStandingConsentRestartAndDisableAreAudited()
    {
        await using var f = new Fixture();
        f.Service.Get().Should().BeNull();
        f.Service.Observe();
        f.Service.Get()!.Enabled.Should().BeFalse();
        await f.Set(true);
        f.Service.Get()!.Enabled.Should().BeTrue();
        var restarted = new RoutingAdviceConfigurationService(new(f.Store), f.Backing.Admission, f.Backing.Audit);
        restarted.Observe();
        restarted.Get()!.Enabled.Should().BeTrue();
        await f.Set(false);
        f.Service.Get()!.Enabled.Should().BeFalse();
        f.Store.Value.Should().Be("Off");
        f.Store.Pending.Should().BeFalse();
        f.Backing.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested,
            SecurityAuditOutcome.Succeeded, SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Backing.Audit.Events.Should().OnlyContain(item => item.ActionId == "configuration.routing-advice");
    }

    [Theory]
    [InlineData("")]
    [InlineData("true")]
    [InlineData("bad")]
    [InlineData("pending")]
    public async Task InvalidOrUnconfirmedPreferencesRemainUnavailable(string value)
    {
        await using var f = new Fixture();
        f.Store.Value = value;
        if (value is "pending") { new LocalRoutingAdvicePreferences(f.Store).BeginWrite(); }
        FluentActions.Invoking(f.Service.Observe).Should().Throw<InvalidDataException>();
        f.Service.Get().Should().BeNull();
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("unobserved")]
    [InlineData("saved")]
    [InlineData("host")]
    [InlineData("model")]
    [InlineData("voice-origin")]
    public async Task OnlyCurrentOriginalInputCanChangeConsent(string boundary)
    {
        await using var f = new Fixture();
        f.Service.Observe();
        var revision = f.Service.Get()!.Revision;
        if (boundary is "stale") { f.Service.Observe(); }
        if (boundary is "unobserved") { f.Store.LoadFailure = new IOException(); FluentActions.Invoking(f.Service.Observe).Should().Throw<IOException>(); }
        if (boundary is "saved") { f.Store.Value = "On"; }
        if (boundary is "host") { f.Backing.Eligible = false; }
        await FluentActions.Awaiting(() => f.Service.SetAsync(true, revision, f.Backing.Policy.Current.Revision, RequestOrigin.LocalUi,
            boundary is "model" ? SecurityAuditInitiator.ModelSuggestion : boundary is "voice-origin"
                ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            f.Backing.Policy, () => f.Backing.Eligible, TestContext.Current.CancellationToken)).Should().ThrowAsync<Exception>();
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
    public async Task PersistenceAuditAndFinalAdmissionFailuresCannotEnableAdvice(string boundary)
    {
        await using var f = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        f.Service.Observe();
        var b = f.Backing;
        await b.Admission.RunAsync(RequestOrigin.LocalUi, static () => true, (_, _) => true, TestContext.Current.CancellationToken);
        f.Store.LoadFailure = boundary is "load" ? new InvalidDataException() : null;
        f.Store.SaveFailure = boundary switch { "save" => new IOException(), "access" => new UnauthorizedAccessException(), _ => null };
        f.Store.AfterSave = () => { if (boundary is "readback") { f.Store.Value = "Off"; } };
        f.Store.ConfirmFailure = boundary is "confirm" ? new IOException() : null;
        f.Store.AfterConfirm = () =>
        {
            if (boundary is "confirmed-readback") { f.Store.Value = "Off"; }
            if (boundary is "confirmed-load") { f.Store.LoadFailure = new IOException(); }
            if (boundary is "confirm-host") { b.Eligible = false; }
        };
        b.Audit.BeforeWrite = item =>
        {
            if (boundary is "requested" && item.Outcome == SecurityAuditOutcome.Requested
                || boundary is "terminal" && item.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException(); }
        };
        b.Tasks.FailTerminal = boundary is "receipt";
        b.Tasks.BeforeCommit = task =>
        {
            if (task.State != HostTaskState.Succeeded) { return; }
            if (boundary is "late-host") { b.Eligible = false; }
            if (boundary is "late-readback") { f.Store.Value = "Off"; }
            if (boundary is "late-cancel") { cancellation.Cancel(); }
        };
        await FluentActions.Awaiting(() => f.Service.SetAsync(true, f.Service.Get()!.Revision, b.Policy.Current.Revision,
            RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser, b.Policy, () => b.Eligible, cancellation.Token))
            .Should().ThrowAsync<Exception>();
        f.Service.Get().Should().BeNull();
    }

    [Fact]
    public async Task ProtectedCallsDenyVoiceAndConcurrentObserveCannotChangeCommittingConsent()
    {
        await using var f = new Fixture();
        f.Service.Observe();
        f.Backing.Call.State = CallState.Active;
        await f.Set(true, RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand);
        f.Store.Writes.Should().Be(0);
        f.Backing.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
        f.Backing.Audit.BeforeWrite = item =>
        {
            if (item.Outcome == SecurityAuditOutcome.Requested)
            { FluentActions.Invoking(f.Service.Observe).Should().Throw<InvalidOperationException>(); }
        };
        await f.Set(true);
        f.Service.Get()!.Enabled.Should().BeTrue();
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        internal Fixture() => Service = new(new(Store), Backing.Admission, Backing.Audit);
        internal SpokenSendReplyConfigurationServiceTests.Fixture Backing { get; } = new();
        internal ForegroundPttConfigurationServiceTests.PreferenceStore Store => Backing.Store;
        internal RoutingAdviceConfigurationService Service { get; }
        internal Task<bool> Set(bool enabled, RequestOrigin origin = RequestOrigin.LocalUi,
            SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser) =>
            Service.SetAsync(enabled, Service.Get()!.Revision, Backing.Policy.Current.Revision, origin, initiator,
                Backing.Policy, () => Backing.Eligible, TestContext.Current.CancellationToken);
        public ValueTask DisposeAsync() => Backing.DisposeAsync();
    }
}
