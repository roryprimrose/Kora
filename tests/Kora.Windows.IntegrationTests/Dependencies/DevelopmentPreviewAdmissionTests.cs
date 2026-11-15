using System.Diagnostics;
using AwesomeAssertions;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Windows.Dependencies;
using Kora.Windows.Audio;
using Kora.Core.Voice;
using Kora.Windows.Skills.RuntimeSetup;

namespace Kora.Windows.IntegrationTests.Dependencies;

[Collection("Host tracing")]
public sealed class DevelopmentPreviewAdmissionTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public DevelopmentPreviewAdmissionTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData(false, RequestOrigin.LocalUi)]
    [InlineData(true, RequestOrigin.HostSystem)]
    [InlineData(true, RequestOrigin.ActivatedVoice)]
    public void Other_builds_and_non_native_requests_cannot_create_preview_authority(bool build, RequestOrigin origin)
    {
        var audit = new Audit();
        using var admission = new DevelopmentPreviewAdmission(build, new Clock(), audit);
        using var root = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Windows, HostOperation.Request);
        Action allow = () => admission.Admit(DevelopmentPreviewCapability.LocalModel, () => true);
        allow.Should().Throw<InvalidOperationException>();
        admission.IsAllowed(DevelopmentPreviewCapability.LocalModel).Should().BeFalse();
        audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("expiry")]
    [InlineData("revoke")]
    [InlineData("clock-backwards")]
    public void Explicit_scoped_preview_is_audited_and_cannot_survive_retirement(string retirement)
    {
        var audit = new Audit();
        var clock = new Clock();
        var eligible = true;
        using var admission = new DevelopmentPreviewAdmission(true, clock, audit);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        admission.Admit(DevelopmentPreviewCapability.LocalModel, () => true, () => eligible);
        var lease = admission.Resolve(DevelopmentPreviewCapability.LocalModel)!;
        var token = lease.Revoked;
        admission.IsAllowed(DevelopmentPreviewCapability.LocalModel).Should().BeTrue();
        admission.IsAllowed(DevelopmentPreviewCapability.Claude).Should().BeFalse();
        audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        if (retirement is "privacy") { eligible = false; }
        if (retirement is "expiry") { clock.Now = clock.Now.AddHours(1); }
        if (retirement is "revoke") { admission.Revoke(); }
        if (retirement is "clock-backwards") { clock.Now = clock.Now.AddMinutes(-1); }
        admission.IsAllowed(DevelopmentPreviewCapability.LocalModel).Should().BeFalse();
        lease.IsCurrent.Should().BeFalse();
        token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void Failed_audit_or_authority_change_never_publishes_preview_admission()
    {
        foreach (var auditFailure in new[] { false, true })
        {
            var live = true;
            var audit = new Audit
            {
                WriteAction = entry =>
                {
                    if (entry.Outcome != SecurityAuditOutcome.Requested) { return; }
                    if (auditFailure) { throw new IOException("synthetic audit failure"); }
                    live = false;
                },
            };
            using var admission = new DevelopmentPreviewAdmission(true, new Clock(), audit);
            using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
            Action allow = () => admission.Admit(DevelopmentPreviewCapability.LocalModel, () => live);
            allow.Should().Throw<Exception>();
            admission.IsAllowed(DevelopmentPreviewCapability.LocalModel).Should().BeFalse();
        }
    }

    [Fact]
    public void Revocation_during_audit_cannot_publish_a_new_lease()
    {
        DevelopmentPreviewAdmission? owner = null;
        var audit = new Audit
        {
            WriteAction = entry => { if (entry.Outcome == SecurityAuditOutcome.Succeeded) { owner!.Revoke(); } },
        };
        using var admission = owner = new DevelopmentPreviewAdmission(true, new Clock(), audit);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        Action allow = () => admission.Admit(DevelopmentPreviewCapability.LocalModel, () => true);
        allow.Should().Throw<InvalidOperationException>();
        admission.IsAllowed(DevelopmentPreviewCapability.LocalModel).Should().BeFalse();
    }

    [Fact]
    public void Original_host_retirement_during_audit_cannot_publish_preview_authority()
    {
        Action? retire = null;
        var audit = new Audit
        {
            WriteAction = entry => { if (entry.Outcome == SecurityAuditOutcome.Succeeded) { retire!(); } },
        };
        using var admission = new DevelopmentPreviewAdmission(true, new Clock(), audit);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        retire = root.Activity!.Stop;
        Action allow = () => admission.Admit(DevelopmentPreviewCapability.LocalModel, () => true);
        allow.Should().Throw<InvalidOperationException>();
        admission.IsAllowed(DevelopmentPreviewCapability.LocalModel).Should().BeFalse();
    }

    [Fact]
    public void Explicit_native_revocation_closes_authority_before_audit_failure()
    {
        var audit = new Audit();
        using var admission = new DevelopmentPreviewAdmission(true, new Clock(), audit);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        admission.Admit(DevelopmentPreviewCapability.LocalModel, () => true);
        audit.WriteAction = entry =>
        {
            if (string.Equals(entry.ActionId, "development-preview.revoke", StringComparison.Ordinal))
            { throw new IOException("synthetic revocation audit failure"); }
        };
        Action revoke = () => admission.RevokeFromNative(() => true);
        revoke.Should().Throw<IOException>();
        admission.IsAllowed(DevelopmentPreviewCapability.LocalModel).Should().BeFalse();
    }
    [Fact]
    public async Task Claude_setup_requires_independent_opt_in_and_binds_one_actual_credential_lease()
    {
        using var admission = new DevelopmentPreviewAdmission(true, new Clock(), new Audit());
        var source = new DevelopmentClaudeDeploymentSource(admission);
        (await source.ResolveAsync(TestContext.Current.CancellationToken)).Should().BeNull();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        admission.Admit(DevelopmentPreviewCapability.Claude, () => true);
        var approval = (await source.ResolveAsync(TestContext.Current.CancellationToken))!;
        var lease = await source.BindCredentialAsync(approval, new string('a', 64), TestContext.Current.CancellationToken);
        lease.Should().NotBeNull();
        (await source.BindCredentialAsync(approval, new string('a', 64), TestContext.Current.CancellationToken)).Should().BeSameAs(lease);
        (await source.BindCredentialAsync(approval, new string('b', 64), TestContext.Current.CancellationToken)).Should().BeNull();
        admission.Revoke();
        (await source.ResolveAsync(TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public void Preview_runtime_receipts_never_claim_release_attestations_and_revoke_with_their_owner()
    {
        using var f = new WindowsOllamaRuntimeResolverTests.Fixture();
        using var admission = new DevelopmentPreviewAdmission(true, f.Time, new Audit());
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        admission.Admit(DevelopmentPreviewCapability.LocalModel, () => true);
        var existing = f.Value!;
        var preview = new WindowsOllamaQualification(Guid.NewGuid(), existing.Catalogue, existing.Runtime,
            existing.RuntimeVersion, existing.Model, existing.ModelDigest, admission.Resolve(DevelopmentPreviewCapability.LocalModel)!);
        preview.QualificationEvidence.Should().BeNull();
        preview.IsDevelopmentPreview.Should().BeTrue();
        preview.IsCurrent(f.Time.GetUtcNow()).Should().BeTrue();
        admission.Revoke();
        preview.IsCurrent(f.Time.GetUtcNow()).Should().BeFalse();
    }

    [Fact]
    public async Task Revoked_wake_preview_cannot_open_capture_and_unprepared_workers_remain_unavailable()
    {
        using var admission = new DevelopmentPreviewAdmission(true, new Clock(), new Audit());
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        admission.Admit(DevelopmentPreviewCapability.Wake | DevelopmentPreviewCapability.BundledWorker, () => true);
        var originalLease = admission.Resolve(DevelopmentPreviewCapability.Wake)!;
        var metadata = Audio.PocketSphinxKeywordConfigurationTests.SyntheticProfile();
        var profile = new HandsFreeNativeProfile(metadata.NativeLibraryPath, metadata.ModelDirectory, metadata.DictionaryPath,
            metadata.VadPath, metadata.Assets, ["Kora"], qualification: null, metadata.ConfigurationFiles,
            previewAdmission: () => originalLease.IsCurrent);
        var stage = new WindowsHandsFreeAudioStage([profile]);
        stage.SupportsProfile("Kora", SpeechInputProfileCatalogue.NativeWakeSelection).Should().BeTrue();
        stage.SupportsProfile("Other", SpeechInputProfileCatalogue.NativeWakeSelection).Should().BeFalse();
        var worker = new DevelopmentBundledWorkerProfile(admission);
        worker.Resolve(new(null, null)).Should().BeNull();
        admission.Revoke();
        admission.Admit(DevelopmentPreviewCapability.Wake, () => true);
        stage.IsAvailable.Should().BeFalse();
        var capture = () => stage.PrepareProfileAsync("Kora", SpeechInputProfileCatalogue.NativeWakeSelection,
            TestContext.Current.CancellationToken);
        await capture.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal Action<SecurityAuditEvent>? WriteAction { get; set; }
        public void Write(SecurityAuditEvent auditEvent) { Events.Add(auditEvent); WriteAction?.Invoke(auditEvent); }
    }
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
