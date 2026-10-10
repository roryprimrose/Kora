using System.Diagnostics;
using System.Text;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Skills;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Skills;

using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Skills;

[Collection("Host tracing")]
public sealed class SharedSkillDiscoveryServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public SharedSkillDiscoveryServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task Explicit_registration_is_audited_and_source_discovery_and_stale_review_are_immutable()
    {
        await using var fixture = new Fixture();
        (await fixture.Service.LoadSourcesAsync(fixture.Eligible, CancellationToken.None)).Should().BeEmpty();
        fixture.Reader.Scans.Should().Be(0);
        var source = await fixture.Register();
        (await fixture.Service.LoadSourcesAsync(fixture.Eligible, CancellationToken.None)).Should().Equal(source);
        fixture.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        var catalogue = await fixture.Discover();
        var snapshot = catalogue.Packages.Single();
        (await fixture.Service.IsCurrentAsync(snapshot, source, fixture.Eligible, CancellationToken.None)).Should().BeTrue();
        fixture.Reader.Text += "\nNew revision";
        (await fixture.Service.IsCurrentAsync(snapshot, source, fixture.Eligible, CancellationToken.None)).Should().BeFalse();
        snapshot.Text.Should().NotContain("New revision");
        fixture.PreferenceStore.Writes.Should().Be(1);
        fixture.Logger.Messages.Should().HaveCountGreaterThan(1).And.OnlyContain(message =>
            !message.Contains("selected root", StringComparison.Ordinal)
            && !message.Contains(fixture.Reader.Text, StringComparison.Ordinal)
            && !message.Contains(source.ProfileRelativeRoot, StringComparison.Ordinal)
            && !message.Contains(snapshot.RevisionDigest, StringComparison.Ordinal));
        fixture.Audit.Events.Should().OnlyContain(item => item.TargetId.StartsWith("shared-source.", StringComparison.Ordinal)
            && !item.TargetId.Contains("profile", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("session")]
    [InlineData("cancel")]
    [InlineData("preferences")]
    public async Task Admission_cancellation_and_concurrent_registration_changes_prevent_save(string changed)
    {
        await using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Reader.AfterSelect = () =>
        {
            if (changed is "privacy") { fixture.Open = false; }
            if (changed is "session") { fixture.Store.Authority = fixture.Store.Authority! with { IsActive = false }; }
            if (changed is "cancel") { cancellation.Cancel(); }
            if (changed is "preferences") { fixture.Preferences.Save([new(Guid.NewGuid(), ".other\\skills", new string('b', 48))]); }
        };
        var action = () => fixture.Service.RegisterAsync("selected root", fixture.Eligible, cancellation.Token);
        if (changed is "cancel") { await action.Should().ThrowAsync<OperationCanceledException>(); }
        else { await action.Should().ThrowAsync<InvalidOperationException>(); }
        fixture.Audit.Events.Should().BeEmpty();
        fixture.PreferenceStore.Writes.Should().Be(changed is "preferences" ? 1 : 0);
    }

    [Fact]
    public async Task Corrupt_consent_blocks_selection_and_reading_without_silent_reset()
    {
        await using var fixture = new Fixture();
        fixture.PreferenceStore.Text = "corrupt";
        await fixture.Register.Should().ThrowAsync<InvalidDataException>();
        fixture.Reader.Selections.Should().Be(0);
        await fixture.Discover.Should().ThrowAsync<InvalidDataException>();
        fixture.Reader.Scans.Should().Be(0);
        fixture.PreferenceStore.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Unregistered_removed_and_duplicate_roots_never_admit_package_reading_or_extra_writes()
    {
        await using var fixture = new Fixture();
        await fixture.Discover.Should().ThrowAsync<InvalidOperationException>();
        fixture.Reader.Scans.Should().Be(0);
        await fixture.Register();
        await fixture.Register.Should().ThrowAsync<InvalidDataException>();
        fixture.PreferenceStore.Writes.Should().Be(1);
        fixture.Reader.AfterScan = () => fixture.Preferences.Save([]);
        await fixture.Discover.Should().ThrowAsync<InvalidOperationException>();
        fixture.Reader.Scans.Should().Be(1);
    }

    [Theory]
    [InlineData("write")]
    [InlineData("read-back")]
    [InlineData("requested-audit")]
    [InlineData("terminal-audit")]
    public async Task Persistence_and_required_audit_failures_never_claim_success(string failure)
    {
        await using var fixture = new Fixture();
        fixture.PreferenceStore.FailWrite = failure is "write";
        if (failure is "read-back") { fixture.PreferenceStore.AfterWrite = () => fixture.PreferenceStore.Text = "{\"Version\":1,\"Sources\":[]}"; }
        if (failure is "requested-audit") { fixture.Audit.BeforeWrite = item => { if (item.Outcome == SecurityAuditOutcome.Requested) { throw new IOException("audit failed"); } }; }
        if (failure is "terminal-audit") { fixture.Audit.BeforeWrite = item => { if (item.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException("audit failed"); } }; }
        var action = fixture.Register;
        await action.Should().ThrowAsync<Exception>();
        fixture.Audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
        if (failure is not "requested-audit") { fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed); }
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task Foreign_original_host_context_does_not_select_a_source_or_gain_authority(RequestOrigin origin)
    {
        await using var fixture = new Fixture();
        using var foreign = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        await fixture.Register.Should().ThrowAsync<InvalidOperationException>();
        fixture.Reader.Selections.Should().Be(0);
    }

    [Fact]
    public async Task Exact_withdrawal_preserves_other_registrations_and_never_reads_a_missing_directory()
    {
        await using var fixture = new Fixture();
        var source = await fixture.Register();
        var other = new SharedSkillSource(Guid.NewGuid(), ".other\\skills", new string('b', 48));
        fixture.Preferences.Save([other, source]);
        var confirmed = await fixture.Service.LoadSourcesAsync(fixture.Eligible, CancellationToken.None);
        var result = await fixture.Service.UnregisterAsync(source, confirmed, fixture.Eligible, CancellationToken.None);
        result.Should().Equal(other);
        fixture.Preferences.Load().Should().Equal(other);
        fixture.Reader.Selections.Should().Be(1);
        fixture.Reader.Scans.Should().Be(0);
        fixture.Audit.Events.TakeLast(2).Select(item => item.ActionId).Should().Equal("skills.source.unregister", "skills.source.unregister");
        fixture.Audit.Events.TakeLast(2).Select(item => item.Outcome)
            .Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        await fixture.Discover.Should().ThrowAsync<InvalidOperationException>();
        var snapshot = SharedSkillSnapshot.Parse(source.Id, "test\\SKILL.md", Encoding.UTF8.GetBytes(fixture.Reader.Text), false);
        var recheck = () => fixture.Service.IsCurrentAsync(snapshot, source, fixture.Eligible, CancellationToken.None);
        await recheck.Should().ThrowAsync<InvalidOperationException>();
        fixture.Reader.Scans.Should().Be(0);
        fixture.PreferenceStore.Text.Should().NotContain(fixture.Reader.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_save_during_requested_audit_is_never_overwritten_or_silently_repaired(bool corrupt)
    {
        await using var fixture = new Fixture();
        var source = await fixture.Register();
        var other = new SharedSkillSource(Guid.NewGuid(), ".other\\skills", new string('b', 48));
        fixture.Audit.BeforeWrite = item =>
        {
            if (string.Equals(item.ActionId, "skills.source.unregister", StringComparison.Ordinal)
                && item.Outcome == SecurityAuditOutcome.Requested)
            {
                if (corrupt) { fixture.PreferenceStore.Text = "unknown"; }
                else { fixture.Preferences.Save([source, other]); }
            }
        };
        var withdrawal = () => fixture.Service.UnregisterAsync(source, [source], fixture.Eligible, CancellationToken.None);
        if (corrupt) { await withdrawal.Should().ThrowAsync<InvalidDataException>(); }
        else { await withdrawal.Should().ThrowAsync<InvalidOperationException>(); }
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed);
        if (corrupt) { fixture.PreferenceStore.Text.Should().Be("unknown"); fixture.PreferenceStore.Writes.Should().Be(1); }
        else { fixture.Preferences.Load().Should().Equal(source, other); fixture.PreferenceStore.Writes.Should().Be(2); }
        await fixture.Discover.Should().ThrowAsync<InvalidOperationException>();
        fixture.Reader.Scans.Should().Be(0);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("directory")]
    [InlineData("path")]
    [InlineData("unknown")]
    [InlineData("stale")]
    [InlineData("duplicate")]
    [InlineData("corrupt")]
    public async Task Foreign_stale_duplicate_unknown_and_corrupt_confirmation_never_mutates_consent(string changed)
    {
        await using var fixture = new Fixture();
        var original = await fixture.Register();
        var source = changed switch
        {
            "id" => new(Guid.NewGuid(), original.ProfileRelativeRoot, original.DirectoryIdentity),
            "directory" => new(original.Id, original.ProfileRelativeRoot, new string('b', 48)),
            "path" => new(original.Id, ".other\\skills", original.DirectoryIdentity),
            "unknown" => new(Guid.NewGuid(), ".other\\skills", new string('b', 48)),
            _ => original,
        };
        IReadOnlyList<SharedSkillSource> confirmed = changed is "duplicate" ? [original, original] : [original];
        if (changed is "stale") { fixture.Preferences.Save([original, new(Guid.NewGuid(), ".other\\skills", new string('b', 48))]); }
        if (changed is "corrupt") { fixture.PreferenceStore.Text = "{\"Version\":9,\"Sources\":[]}"; }
        var before = fixture.PreferenceStore.Text;
        var writes = fixture.PreferenceStore.Writes;
        var action = () => fixture.Service.UnregisterAsync(source, confirmed, fixture.Eligible, CancellationToken.None);
        if (changed is "duplicate" or "corrupt") { await action.Should().ThrowAsync<InvalidDataException>(); }
        else { await action.Should().ThrowAsync<InvalidOperationException>(); }
        fixture.PreferenceStore.Text.Should().Be(before);
        fixture.PreferenceStore.Writes.Should().Be(writes);
        fixture.Audit.Events.Should().HaveCount(2);
        fixture.Reader.Scans.Should().Be(0);
    }

    [Theory]
    [InlineData("write")]
    [InlineData("read-back")]
    [InlineData("read-back-throws")]
    [InlineData("requested-audit")]
    [InlineData("terminal-audit")]
    [InlineData("failed-audit")]
    public async Task Withdrawal_failure_is_not_rollback_and_closes_read_authority_until_fresh_observation(string failure)
    {
        await using var fixture = new Fixture();
        var source = await fixture.Register();
        var before = fixture.Preferences.Load();
        fixture.PreferenceStore.FailWrite = failure is "write" or "failed-audit";
        if (failure is "read-back") { fixture.PreferenceStore.AfterWrite = () => fixture.PreferenceStore.Text = "corrupt"; }
        if (failure is "read-back-throws") { fixture.PreferenceStore.AfterWrite = () => fixture.PreferenceStore.FailRead = true; }
        fixture.Audit.BeforeWrite = item =>
        {
            if (!string.Equals(item.ActionId, "skills.source.unregister", StringComparison.Ordinal)) { return; }
            if (failure is "requested-audit" && item.Outcome == SecurityAuditOutcome.Requested
                || failure is "terminal-audit" && item.Outcome == SecurityAuditOutcome.Succeeded
                || failure is "failed-audit" && item.Outcome == SecurityAuditOutcome.Failed)
            { throw new IOException("audit failed"); }
        };
        var withdraw = () => fixture.Service.UnregisterAsync(source, before, fixture.Eligible, CancellationToken.None);
        await withdraw.Should().ThrowAsync<Exception>();
        fixture.Audit.Events.Should().NotContain(item => item.ActionId == "skills.source.unregister" && item.Outcome == SecurityAuditOutcome.Succeeded);
        if (failure is not "requested-audit")
        {
            await fixture.Discover.Should().ThrowAsync<InvalidOperationException>();
            await fixture.Register.Should().ThrowAsync<InvalidOperationException>();
        }
        fixture.Reader.Scans.Should().Be(0);
        fixture.PreferenceStore.FailRead = false;
        if (failure is "terminal-audit") { fixture.Preferences.Load().Should().BeEmpty(); }
        if (failure is "write" or "failed-audit") { fixture.Preferences.Load().Should().Equal(source); }
        if (failure is "read-back")
        {
            var refresh = () => fixture.Service.LoadSourcesAsync(fixture.Eligible, CancellationToken.None);
            await refresh.Should().ThrowAsync<InvalidDataException>();
            await fixture.Discover.Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            await fixture.Service.LoadSourcesAsync(fixture.Eligible, CancellationToken.None);
            if (failure is not "requested-audit") { await fixture.Discover.Should().ThrowAsync<InvalidOperationException>(); }
        }
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("ownership")]
    [InlineData("generation")]
    [InlineData("cancel")]
    [InlineData("disposed")]
    [InlineData("no-intent")]
    public async Task Withdrawal_requires_live_original_native_intent_owner_privacy_generation_and_lifetime(string failure)
    {
        await using var fixture = new Fixture();
        var source = await fixture.Register();
        using var cancellation = new CancellationTokenSource();
        if (failure is "privacy" or "ownership") { fixture.Open = false; }
        if (failure is "generation") { fixture.Store.Authority = fixture.Store.Authority! with { Generation = new(2) }; }
        if (failure is "cancel") { cancellation.Cancel(); }
        if (failure is "disposed") { await fixture.Admission.DisposeAsync(); }
        if (failure is "no-intent") { fixture.Store.BeforeControlIntent = _ => throw new InvalidOperationException("No durable native intent."); }
        var withdraw = () => fixture.Service.UnregisterAsync(source, [source], fixture.Eligible, cancellation.Token);
        await withdraw.Should().ThrowAsync<Exception>();
        fixture.PreferenceStore.Writes.Should().Be(1);
        fixture.Preferences.Load().Should().Equal(source);
        fixture.Reader.Scans.Should().Be(0);
        fixture.Audit.Events.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task Non_native_original_origin_cannot_withdraw_consent(RequestOrigin origin)
    {
        await using var fixture = new Fixture();
        var source = await fixture.Register();
        using var foreign = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        var withdraw = () => fixture.Service.UnregisterAsync(source, [source], fixture.Eligible, CancellationToken.None);
        await withdraw.Should().ThrowAsync<InvalidOperationException>();
        fixture.Preferences.Load().Should().Equal(source);
        fixture.PreferenceStore.Writes.Should().Be(1);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("privacy")]
    public async Task Admission_change_after_durable_withdrawal_does_not_claim_success_or_rollback(string changed)
    {
        await using var fixture = new Fixture();
        var source = await fixture.Register();
        using var cancellation = new CancellationTokenSource();
        fixture.PreferenceStore.AfterWrite = () =>
        {
            if (changed is "cancel") { cancellation.Cancel(); }
            else { fixture.Open = false; }
        };
        var withdraw = () => fixture.Service.UnregisterAsync(source, [source], fixture.Eligible, cancellation.Token);
        await withdraw.Should().ThrowAsync<Exception>();
        fixture.Preferences.Load().Should().BeEmpty();
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed);
        fixture.Open = true;
        await fixture.Discover.Should().ThrowAsync<InvalidOperationException>();
        fixture.Reader.Scans.Should().Be(0);
    }

    [Fact]
    public async Task Cancelled_in_flight_read_cannot_publish_and_reregistration_never_revives_old_identity_or_snapshot()
    {
        await using var fixture = new Fixture();
        var oldSource = await fixture.Register();
        var oldSnapshot = (await fixture.Discover()).Packages.Single();
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Reader.DuringScan = async () => { reading.SetResult(); await release.Task; };
        using var cancelled = new CancellationTokenSource();
        var pending = fixture.Service.DiscoverAsync(oldSource, fixture.Eligible, cancelled.Token);
        await reading.Task;
        await cancelled.CancelAsync();
        var withdrawing = fixture.Service.UnregisterAsync(oldSource, [oldSource], fixture.Eligible, CancellationToken.None);
        release.SetResult();
        await ((Func<Task>)(async () => await pending)).Should().ThrowAsync<OperationCanceledException>();
        (await withdrawing).Should().BeEmpty();
        fixture.Reader.DuringScan = null;
        fixture.Reader.Source = new(Guid.NewGuid(), oldSource.ProfileRelativeRoot, oldSource.DirectoryIdentity);
        var newSource = await fixture.Register();
        newSource.Id.Should().NotBe(oldSource.Id);
        await fixture.Discover();
        var oldRead = () => fixture.Service.DiscoverAsync(oldSource, fixture.Eligible, CancellationToken.None);
        await oldRead.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.Service.IsCurrentAsync(oldSnapshot, newSource, fixture.Eligible, CancellationToken.None)).Should().BeFalse();
        var staleWithdrawal = () => fixture.Service.UnregisterAsync(oldSource, [oldSource], fixture.Eligible, CancellationToken.None);
        await staleWithdrawal.Should().ThrowAsync<InvalidOperationException>();
        fixture.Preferences.Load().Should().Equal(newSource);
    }

    [Fact]
    public async Task Restored_old_registration_and_reused_reader_identity_cannot_regrant_withdrawn_read_consent()
    {
        await using var fixture = new Fixture();
        var source = await fixture.Register();
        await fixture.Service.UnregisterAsync(source, [source], fixture.Eligible, CancellationToken.None);
        fixture.Preferences.Save([source]);
        (await fixture.Service.LoadSourcesAsync(fixture.Eligible, CancellationToken.None)).Should().Equal(source);
        await fixture.Discover.Should().ThrowAsync<InvalidOperationException>();
        fixture.Reader.Scans.Should().Be(0);
        await fixture.Service.UnregisterAsync(source, [source], fixture.Eligible, CancellationToken.None);
        await fixture.Register.Should().ThrowAsync<InvalidOperationException>();
        fixture.Preferences.Load().Should().BeEmpty();
        fixture.Reader.Source = new(Guid.NewGuid(), source.ProfileRelativeRoot, source.DirectoryIdentity);
        await fixture.Register();
        await fixture.Discover();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal AudioControlTestStore Store { get; } = new();
        internal LocalSharedSkillPreferencesTests.Store PreferenceStore { get; } = new();
        internal LocalSharedSkillPreferences Preferences { get; }
        internal Reader Reader { get; } = new();
        internal Audit Audit { get; } = new();
        internal SharedSkillAdmission Admission { get; }
        internal SharedSkillDiscoveryService Service { get; }
        internal CaptureLogger Logger { get; } = new();
        internal bool Open { get; set; } = true;
        internal bool Eligible() => Open;
        internal Fixture()
        {
            Preferences = new(PreferenceStore);
            Admission = new(Store, Store, new HostTaskCoordinator(Store));
            Service = new(Preferences, Reader, Admission, Audit, Logger);
        }
        internal Func<Task<SharedSkillSource>> Register => () => Service.RegisterAsync("selected root", Eligible, CancellationToken.None);
        internal Func<Task<SharedSkillCatalogue>> Discover => () => Service.DiscoverAsync(Reader.Source, Eligible, CancellationToken.None);
        public ValueTask DisposeAsync() => Admission.DisposeAsync();
    }

    private sealed class Reader : ISharedSkillSourceReader
    {
        internal SharedSkillSource Source { get; set; } = new(Guid.NewGuid(), ".agents\\skills", new string('a', 48));
        internal string Text { get; set; } = "---\nname: test\nversion: 1.0.0\ndescription: Local inspect\n---\nInstructions";
        internal int Selections { get; private set; }
        internal int Scans { get; private set; }
        internal Action? AfterSelect { get; set; }
        internal Action? AfterScan { get; set; }
        internal Func<Task>? DuringScan { get; set; }
        public ValueTask<SharedSkillSource> SelectAsync(string selectedRoot, CancellationToken cancellationToken)
        { Selections++; AfterSelect?.Invoke(); return ValueTask.FromResult(Source); }
        public async ValueTask<SharedSkillCatalogue> DiscoverAsync(SharedSkillSource source, CancellationToken cancellationToken)
        {
            Scans++;
            AfterScan?.Invoke();
            if (DuringScan is { } during) { await during(); }
            return new SharedSkillCatalogue(Source,
                [SharedSkillSnapshot.Parse(Source.Id, "test\\SKILL.md", Encoding.UTF8.GetBytes(Text), false)]);
        }
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal Action<SecurityAuditEvent>? BeforeWrite { get; set; }
        public void Write(SecurityAuditEvent auditEvent) { BeforeWrite?.Invoke(auditEvent); Events.Add(auditEvent); }
    }

    private sealed class CaptureLogger : ILogger<SharedSkillDiscoveryService>
    {
        internal List<string> Messages { get; } = [];
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
