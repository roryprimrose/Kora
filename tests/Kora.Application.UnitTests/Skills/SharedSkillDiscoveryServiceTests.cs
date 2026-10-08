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
        internal SharedSkillSource Source { get; } = new(Guid.NewGuid(), ".agents\\skills", new string('a', 48));
        internal string Text { get; set; } = "---\nname: test\nversion: 1.0.0\ndescription: Local inspect\n---\nInstructions";
        internal int Selections { get; private set; }
        internal int Scans { get; private set; }
        internal Action? AfterSelect { get; set; }
        internal Action? AfterScan { get; set; }
        public ValueTask<SharedSkillSource> SelectAsync(string selectedRoot, CancellationToken cancellationToken)
        { Selections++; AfterSelect?.Invoke(); return ValueTask.FromResult(Source); }
        public ValueTask<SharedSkillCatalogue> DiscoverAsync(SharedSkillSource source, CancellationToken cancellationToken)
        {
            Scans++;
            AfterScan?.Invoke();
            return ValueTask.FromResult(new SharedSkillCatalogue(Source,
                [SharedSkillSnapshot.Parse(Source.Id, "test\\SKILL.md", Encoding.UTF8.GetBytes(Text), false)]));
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
