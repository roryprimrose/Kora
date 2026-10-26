using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Application.Communication;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class CommunicationActivityTests
{
    [Fact]
    public async Task Explicit_selection_is_audited_persistent_and_independent_of_manual_and_grant_policy()
    {
        await using var f = new CommunicationActivityFixture();
        await f.Initialize();
        f.Observations.CurrentState.Should().Be(CallState.Unavailable);
        (await f.Select(CommunicationActivitySelection.None)).Should().Be(CallMutationOutcome.Unchanged);
        (await f.Select(CommunicationActivitySelection.WindowsCommunicationsAudio)).Should().Be(CallMutationOutcome.Applied);
        f.Preferences.Load().Should().Be(CommunicationActivitySelection.WindowsCommunicationsAudio);
        f.Observations.CurrentState.Should().Be(CallState.Unknown);
        f.Source.Signal(CallState.Suspected, f.Time.GetUtcNow());
        f.Policy.Current.SuppressSpeech.Should().BeTrue();
        f.Policy.Current.Authorization(true, true).AllowsReusableGrants.Should().BeTrue();
        f.Policy.SetManual(true, RequestOrigin.LocalUi, f.Policy.Current.Revision, () => f.Eligible)
            .Should().Be(CallMutationOutcome.Applied);
        f.Source.Signal(CallState.Clear, f.Time.GetUtcNow());
        f.Policy.Current.ManualActive.Should().BeTrue();
        f.Policy.Current.SuppressSpeech.Should().BeTrue();
        f.Policy.SetManual(false, RequestOrigin.LocalUi, f.Policy.Current.Revision, () => f.Eligible);
        f.Policy.Current.SuppressSpeech.Should().BeFalse();
        f.Audit.Events.Should().Contain(e => e.Outcome == SecurityAuditOutcome.Succeeded);
        f.Audit.Requests.Should().OnlyContain(request => request != null && request.SessionId == f.Authority.Authority!.SessionId);
        (await f.Select(CommunicationActivitySelection.None)).Should().Be(CallMutationOutcome.Applied);
        f.Preferences.Load().Should().Be(CommunicationActivitySelection.None);
        f.Observations.CurrentState.Should().Be(CallState.Unavailable);
        f.Source.Releases.Should().Be(1);
    }

    [Theory]
    [InlineData(CallState.Suspected)]
    [InlineData(CallState.Unknown)]
    public async Task Protected_original_voice_selection_is_denied_without_mutation_and_new_native_selection_remains_possible(CallState state)
    {
        await using var f = new CommunicationActivityFixture();
        await f.Initialize(CommunicationActivitySelection.WindowsCommunicationsAudio);
        f.Source.Signal(state, f.Time.GetUtcNow());
        (await f.Select(CommunicationActivitySelection.None, RequestOrigin.ActivatedVoice))
            .Should().Be(CallMutationOutcome.OriginDenied);
        f.Preferences.Load().Should().Be(CommunicationActivitySelection.WindowsCommunicationsAudio);
        (await f.Select(CommunicationActivitySelection.None)).Should().Be(CallMutationOutcome.Applied);
        f.Audit.Events.Should().Contain(e => e.Outcome == SecurityAuditOutcome.Denied);
    }

    [Theory]
    [InlineData("unsupported")]
    [InlineData("stale")]
    [InlineData("host")]
    [InlineData("unknown-origin")]
    [InlineData("pending")]
    [InlineData("saved")]
    [InlineData("cancelled")]
    public async Task Unavailable_or_stale_admission_cannot_enable_a_source(string stage)
    {
        await using var f = new CommunicationActivityFixture();
        await f.Initialize();
        if (stage is "unsupported") { f.Source.IsQualified = false; }
        if (stage is "host") { f.Eligible = false; }
        if (stage is "pending") { f.Preferences.BeginWrite(); }
        if (stage is "saved") { f.Preferences.Save(CommunicationActivitySelection.WindowsCommunicationsAudio); }
        using var cancelled = new CancellationTokenSource();
        if (stage is "cancelled") { await cancelled.CancelAsync(); }
        var operation = () => f.Select(CommunicationActivitySelection.WindowsCommunicationsAudio,
            stage is "unknown-origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi,
            stage is "stale" ? -1 : null, stage is "cancelled" ? cancelled.Token : null);
        if (stage is "pending" or "saved" or "host" or "unknown-origin" or "cancelled")
        { await operation.Should().ThrowAsync<Exception>(); }
        else { (await operation()).Should().NotBe(CallMutationOutcome.Applied); }
        f.Source.Opens.Should().Be(0);
    }

    [Theory]
    [InlineData("marker")]
    [InlineData("save")]
    [InlineData("readback")]
    [InlineData("terminal-audit")]
    [InlineData("confirmation")]
    [InlineData("after-confirmation")]
    public async Task Preference_and_receipt_failures_hold_unknown_without_start_or_silent_recovery(string stage)
    {
        await using var f = new CommunicationActivityFixture();
        await f.Initialize();
        f.Store.BeforeWrite = name =>
        {
            if (stage is "marker" && name.Contains("unconfirmed", StringComparison.Ordinal)
                || stage is "save" && name is "communication-activity.txt"
                || stage is "confirmation" && name.Contains("unconfirmed", StringComparison.Ordinal)
                    && f.Store.Values.ContainsKey("communication-activity.txt"))
            { throw new IOException("deterministic failure"); }
        };
        f.Store.AfterWrite = name =>
        {
            if (stage is "readback" && name is "communication-activity.txt")
            { f.Store.Values[name] = "1\nNone"; }
            if (stage is "after-confirmation" && name is "communication-activity-unconfirmed.txt"
                && !f.Store.Values.ContainsKey(name)) { f.Eligible = false; }
        };
        f.Audit.BeforeWrite = audit =>
        {
            if (stage is "terminal-audit" && audit.Outcome == SecurityAuditOutcome.Succeeded)
            { throw new IOException("deterministic audit failure"); }
        };
        await FluentActions.Awaiting(() => f.Select(CommunicationActivitySelection.WindowsCommunicationsAudio)).Should().ThrowAsync<Exception>();
        f.Observations.CurrentState.Should().Be(CallState.Unknown);
        f.Observations.PreferencesAvailable.Should().BeFalse();
        f.Source.Opens.Should().Be(0);
        f.Store.BeforeWrite = null;
        f.Store.AfterWrite = null;
    }

    [Theory]
    [InlineData("")]
    [InlineData("2\nWindowsCommunicationsAudio")]
    [InlineData("1\nWindowsCommunicationsAudio\nextra")]
    [InlineData("1\nwindowscommunicationsaudio")]
    [InlineData("1\n0")]
    public async Task Malformed_persisted_source_is_not_reinterpreted_as_disabled(string text)
    {
        await using var f = new CommunicationActivityFixture();
        f.Store.Values["communication-activity.txt"] = text;
        FluentActions.Invoking(f.Observations.ObservePreferences).Should().Throw<InvalidDataException>();
        f.Observations.CurrentState.Should().Be(CallState.Unknown);
        f.Observations.PreferencesAvailable.Should().BeFalse();
        await f.Observations.JoinAsync();
    }

    [Fact]
    public async Task Invalid_enum_pending_marker_and_public_preference_constructor_preserve_explicit_failure()
    {
        await using var f = new CommunicationActivityFixture();
        FluentActions.Invoking(() => f.Preferences.Save((CommunicationActivitySelection)99))
            .Should().Throw<ArgumentOutOfRangeException>();
        await FluentActions.Awaiting(() => f.Select((CommunicationActivitySelection)99))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
        f.Preferences.BeginWrite();
        FluentActions.Invoking(() => f.Preferences.Load()).Should().Throw<InvalidDataException>();
        f.Preferences.ConfirmWrite();
        f.Preferences.Load().Should().Be(CommunicationActivitySelection.None);
        new LocalCommunicationActivityPreferences(new MissingPaths()).Load().Should().Be(CommunicationActivitySelection.None);
    }

    [Fact]
    public async Task Concurrent_selection_cannot_reuse_a_pending_control_session()
    {
        await using var f = new CommunicationActivityFixture();
        await f.Initialize();
        var pending = new TaskCompletionSource<WorkSessionAuthorization>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Authority.PendingCreation = pending.Task;
        var first = f.Select(CommunicationActivitySelection.WindowsCommunicationsAudio);
        await FluentActions.Awaiting(() => f.Select(CommunicationActivitySelection.None))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*already pending*");
        pending.SetResult(f.Authority.Authority!);
        (await first).Should().Be(CallMutationOutcome.Applied);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("saved")]
    [InlineData("policy")]
    public async Task Post_receipt_authority_preference_and_policy_changes_cannot_activate_the_source(string stage)
    {
        await using var f = new CommunicationActivityFixture();
        await f.Initialize();
        var terminal = false;
        f.Audit.BeforeWrite = value =>
        {
            if (value.Outcome != SecurityAuditOutcome.Succeeded) { return; }
            terminal = true;
            if (stage is "saved") { f.Store.Values["communication-activity.txt"] = "1\nNone"; }
            if (stage is "policy")
            { f.Policy.SetManual(true, RequestOrigin.LocalUi, f.Policy.Current.Revision, () => true); }
        };
        bool Eligible() => stage is not "owner" || !terminal || HostActivity.Current is not null;
        await FluentActions.Awaiting(() => f.Configuration.SelectAsync(
            CommunicationActivitySelection.WindowsCommunicationsAudio, RequestOrigin.LocalUi, f.Policy.Current.Revision,
            f.Policy, Eligible, f.Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Observations.CurrentState.Should().Be(CallState.Unknown);
        f.Source.Opens.Should().Be(0);
        FluentActions.Invoking(f.Preferences.Load).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task Configuration_and_source_diagnostics_are_generated_content_free_and_fail_closed()
    {
        var hostLog = new CommunicationActivityFixture.EnabledLogger<CommunicationActivityCallStateService>();
        var settingLog = new CommunicationActivityFixture.EnabledLogger<CommunicationActivityConfigurationService>();
        await using var f = new CommunicationActivityFixture(hostLog, settingLog);
        f.Store.BeforeRead = _ => throw new IOException("private-device-or-content");
        FluentActions.Invoking(f.Observations.ObservePreferences).Should().Throw<IOException>();
        await f.Observations.JoinAsync();
        f.Store.BeforeRead = null;
        await f.Initialize();
        f.Store.BeforeRead = _ => throw new IOException("private-device-or-content");
        await FluentActions.Awaiting(() => f.Select(CommunicationActivitySelection.WindowsCommunicationsAudio))
            .Should().ThrowAsync<IOException>();
        f.Store.BeforeRead = null;
        await f.Initialize();
        f.Source.OpenFailure = new IOException("private-device-or-content");
        await f.Select(CommunicationActivitySelection.WindowsCommunicationsAudio);
        await f.Observations.JoinAsync();
        hostLog.Messages.Should().HaveCountGreaterThanOrEqualTo(2);
        settingLog.Messages.Should().NotBeEmpty();
        hostLog.Messages.Concat(settingLog.Messages).Should()
            .OnlyContain(message => !message.Contains("private-device-or-content", StringComparison.Ordinal));
        f.Observations.CurrentState.Should().Be(CallState.Unknown);
    }

    private sealed class MissingPaths : IApplicationDataPaths
    {
        private readonly string path = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts", Guid.NewGuid().ToString("N"));
        public string LocalRoot => path;
        public string RoamingRoot => path;
    }
}