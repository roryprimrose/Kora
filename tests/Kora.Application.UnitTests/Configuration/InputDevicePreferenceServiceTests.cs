using System.Diagnostics;

using AwesomeAssertions;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class InputDevicePreferenceServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public InputDevicePreferenceServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public void Existing_atomic_preference_format_restarts_and_reset_clears_only_input()
    {
        var store = new Store();
        var preferences = new LocalAudioDevicePreferences(store, NullLogger<LocalAudioDevicePreferences>.Instance);
        var audit = new Audit();
        var service = new InputDevicePreferenceService(preferences, audit);
        using var policy = new CallCommunicationPolicy(new Call());
        service.Source.Should().Be("unavailable");
        service.Load().Should().BeNull();
        service.Source.Should().Be("default");
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Storage);
        service.Apply(new("exact", "Private endpoint"), activity.Request, 0, SecurityAuditInitiator.TypedCommand,
            policy, static () => true, static () => { }).Should().BeTrue();
        new InputDevicePreferenceService(preferences, audit).Load().Should().Be("exact");
        service.Source.Should().Be("saved");
        service.Apply(SystemAudioDevices.Microphone, activity.Request, 0, SecurityAuditInitiator.LocalUser,
            policy, static () => true, static () => { }).Should().BeTrue();
        new InputDevicePreferenceService(preferences, audit).Load().Should().BeNull();
        store.Values["output-device-id.txt"].Should().Be("retained");
        service.Source.Should().Be("default");
        audit.Events.Should().HaveCount(4);
        audit.Events.Chunk(2).Should().OnlyContain(pair => pair[0].Outcome == SecurityAuditOutcome.Requested
            && pair[1].Outcome == SecurityAuditOutcome.Succeeded && pair[0].CorrelationId == pair[1].CorrelationId);
        audit.Events.Should().OnlyContain(item => string.Equals(item.TargetId, "preferences.device-local", StringComparison.Ordinal));
    }

    [Fact]
    public void Malformed_persisted_state_is_not_reinterpreted_as_System_and_failed_reload_has_unavailable_source()
    {
        var store = new Store();
        var service = new InputDevicePreferenceService(
            new LocalAudioDevicePreferences(store, NullLogger<LocalAudioDevicePreferences>.Instance), new Audit());
        store.Values["microphone-id.txt"] = " exact ";
        service.Load().Should().Be("exact");
        store.Values["microphone-id.txt"] = " ";
        var load = service.Load;
        load.Should().Throw<InvalidDataException>();
        service.Source.Should().Be("unavailable");
        store.Values["microphone-id.txt"] = "exact";
        store.ReadFailure = new IOException("access unavailable");
        load.Should().Throw<IOException>();
        service.Source.Should().Be("unavailable");
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("expired")]
    [InlineData("foreign")]
    [InlineData("equal")]
    [InlineData("host")]
    [InlineData("stale-call")]
    [InlineData("voice")]
    public void Missing_expired_equal_lookalike_cross_session_or_stale_host_context_cannot_commit(string kind)
    {
        var store = new Store();
        var audit = new Audit();
        var service = new InputDevicePreferenceService(
            new LocalAudioDevicePreferences(store, NullLogger<LocalAudioDevicePreferences>.Instance), audit);
        var call = new Call { CurrentState = string.Equals(kind, "voice", StringComparison.Ordinal) ? CallState.Unknown : CallState.Clear };
        using var policy = new CallCommunicationPolicy(call);
        var request = HostRequest.Create(string.Equals(kind, "voice", StringComparison.Ordinal) ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        if (string.Equals(kind, "absent", StringComparison.Ordinal))
        {
            service.Apply(SystemAudioDevices.Microphone, request, 0, SecurityAuditInitiator.TypedCommand,
                policy, static () => true, static () => { }).Should().BeFalse();
        }
        else
        {
            using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Storage);
            var proposal = kind switch
            {
                "foreign" => HostRequest.Create(RequestOrigin.LocalUi),
                "equal" => new(request.RequestId, request.SessionId, request.TaskId, request.Origin),
                _ => request,
            };
            if (string.Equals(kind, "expired", StringComparison.Ordinal)) { activity.Dispose(); }
            service.Apply(SystemAudioDevices.Microphone, proposal, string.Equals(kind, "stale-call", StringComparison.Ordinal) ? 1 : 0,
                SecurityAuditInitiator.LocalUser, policy, () => !string.Equals(kind, "host", StringComparison.Ordinal), static () => { }).Should().BeFalse();
        }
        store.Values.Should().NotContainKey("microphone-id.txt");
        audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
    }

    [Fact]
    public void Reentrant_writes_and_reads_are_not_new_authority_during_commit()
    {
        var store = new Store();
        var audit = new Audit();
        var service = new InputDevicePreferenceService(
            new LocalAudioDevicePreferences(store, NullLogger<LocalAudioDevicePreferences>.Instance), audit);
        using var policy = new CallCommunicationPolicy(new Call());
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Storage);
        audit.BeforeWrite = item =>
        {
            if (item.Outcome != SecurityAuditOutcome.Requested) { return; }
            service.Apply(SystemAudioDevices.Microphone, activity.Request, 0, SecurityAuditInitiator.LocalUser,
                policy, static () => true, static () => { }).Should().BeFalse();
            var load = service.Load;
            load.Should().Throw<InvalidOperationException>();
        };
        service.Apply(new("exact", "Private"), activity.Request, 0, SecurityAuditInitiator.LocalUser,
            policy, static () => true, static () => { }).Should().BeTrue();
        audit.Events.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("requested")]
    [InlineData("terminal")]
    [InlineData("storage")]
    public void Evidence_or_atomic_replacement_failure_never_publishes_successful_preference(string stage)
    {
        var store = new Store();
        store.Values["microphone-id.txt"] = "old";
        var audit = new Audit();
        var service = new InputDevicePreferenceService(
            new LocalAudioDevicePreferences(store, NullLogger<LocalAudioDevicePreferences>.Instance), audit);
        service.Load().Should().Be("old");
        using var policy = new CallCommunicationPolicy(new Call());
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Storage);
        audit.BeforeWrite = item =>
        {
            if (string.Equals(stage, "requested", StringComparison.Ordinal) && item.Outcome == SecurityAuditOutcome.Requested
                || string.Equals(stage, "terminal", StringComparison.Ordinal) && item.Outcome == SecurityAuditOutcome.Succeeded)
            { throw new IOException("evidence unavailable"); }
        };
        if (string.Equals(stage, "storage", StringComparison.Ordinal)) { store.WriteFailure = new IOException("replacement unavailable"); }
        var apply = () => service.Apply(SystemAudioDevices.Microphone, activity.Request, 0,
            SecurityAuditInitiator.LocalUser, policy, static () => true, static () => { });
        apply.Should().Throw<IOException>();
        service.Source.Should().Be(string.Equals(stage, "terminal", StringComparison.Ordinal) ? "unavailable" : "saved");
        if (string.Equals(stage, "terminal", StringComparison.Ordinal)) { store.Values.Should().NotContainKey("microphone-id.txt"); service.DesiredId.Should().BeNull(); }
        else { store.Values["microphone-id.txt"].Should().Be("old"); }
        if (string.Equals(stage, "storage", StringComparison.Ordinal)) { audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed); }
    }

    private sealed class Store : IPreferenceStore
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal) { ["output-device-id.txt"] = "retained" };
        public IOException? ReadFailure { get; set; }
        public IOException? WriteFailure { get; set; }
        public string? ReadText(string fileName) => ReadFailure is { } failure ? throw failure : Values.GetValueOrDefault(fileName);
        public string[]? ReadLines(string fileName) => throw new NotSupportedException();
        public void WriteText(string fileName, string contents)
        {
            if (WriteFailure is { } failure) { throw failure; }
            Values[fileName] = contents;
        }
        public void WriteLines(string fileName, IEnumerable<string> contents) => throw new NotSupportedException();
        public void Delete(string fileName)
        {
            if (WriteFailure is { } failure) { throw failure; }
            Values.Remove(fileName);
        }
    }

    private sealed class Audit : ISecurityAuditLog
    {
        public List<SecurityAuditEvent> Events { get; } = [];
        public Action<SecurityAuditEvent>? BeforeWrite { get; set; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            BeforeWrite?.Invoke(auditEvent);
            Events.Add(auditEvent);
        }
    }

    private sealed class Call : ICallStateService
    {
        public CallState CurrentState { get; init; } = CallState.Clear;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged { add { } remove { } }
    }
}
