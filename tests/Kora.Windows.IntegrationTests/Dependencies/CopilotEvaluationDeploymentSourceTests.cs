using System.Diagnostics;
using AwesomeAssertions;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Dependencies;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Dependencies;

[Collection("Host tracing")]
public sealed class CopilotEvaluationDeploymentSourceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public CopilotEvaluationDeploymentSourceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Observation_does_not_admit_execution_and_explicit_approval_pins_bytes_in_private_paths()
    {
        using var paths = new OwnedStorageFixture();
        var executable = Path.Combine(paths.LocalRoot, "copilot.exe");
        await File.WriteAllTextAsync(executable, "synthetic-runtime-not-executed", Token);
        var audit = new Audit();
        using var source = new CopilotEvaluationDeploymentSource(paths, new Clock(), audit,
            NullLogger<CopilotEvaluationDeploymentSource>.Instance);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        (await source.ResolveAsync(Token)).Should().BeNull();
        var reviewed = await source.ObserveAsync(executable, () => true, Token);
        (await source.ResolveAsync(Token)).Should().BeNull();
        Action replace = () => File.WriteAllText(executable, "replaced");
        replace.Should().Throw<IOException>();
        source.Admit(reviewed, () => true, () => true, Token);
        var approved = (await source.ResolveAsync(Token))!;
        approved.IsEvaluation.Should().BeTrue();
        approved.ExecutablePath.Should().Be(executable);
        approved.BaseDirectory.Should().StartWith(paths.LocalRoot + Path.DirectorySeparatorChar);
        approved.Environment["TEMP"].Should().Be(approved.WorkingDirectory);
        approved.Environment.Keys.Should().BeEquivalentTo("SystemRoot", "WINDIR", "TEMP", "TMP");
        (await source.ResolveAsync(Token)).Should().BeSameAs(approved);
        new RestrictedStorageDirectory(paths, includeKeys: false,
            partitionName: Path.GetFileName(approved.BaseDirectory)).Verify();
        audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        source.Revoke();
        (await source.ResolveAsync(Token)).Should().BeNull();
        await File.WriteAllTextAsync(executable, "replacement-after-revocation", Token);
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("privacy")]
    [InlineData("revoke")]
    public async Task A_live_evaluation_cannot_survive_expiry_privacy_loss_or_revocation(string reason)
    {
        using var paths = new OwnedStorageFixture();
        var executable = Path.Combine(paths.LocalRoot, "copilot.exe");
        await File.WriteAllTextAsync(executable, "synthetic", Token);
        var clock = new Clock();
        var live = true;
        using var source = new CopilotEvaluationDeploymentSource(paths, clock, new Audit(),
            NullLogger<CopilotEvaluationDeploymentSource>.Instance);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        var reviewed = await source.ObserveAsync(executable, () => live, Token);
        source.Admit(reviewed, () => live, () => live, Token);
        if (reason is "expiry") { clock.Now = clock.Now.AddHours(1); }
        if (reason is "privacy") { live = false; }
        if (reason is "revoke") { source.Revoke(); }
        (await source.ResolveAsync(Token)).Should().BeNull();
        await File.WriteAllTextAsync(executable, "no-review-lock-remains", Token);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("expired-review")]
    [InlineData("cancelled")]
    [InlineData("audit")]
    [InlineData("privacy")]
    public async Task Failed_approval_never_publishes_a_deployment_or_keeps_the_runtime_lock(string fault)
    {
        using var paths = new OwnedStorageFixture();
        var executable = Path.Combine(paths.LocalRoot, "copilot.exe");
        await File.WriteAllTextAsync(executable, "synthetic", Token);
        var clock = new Clock();
        var audit = new Audit();
        using var source = new CopilotEvaluationDeploymentSource(paths, clock, audit,
            NullLogger<CopilotEvaluationDeploymentSource>.Instance);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        var reviewed = await source.ObserveAsync(executable, () => true, Token);
        if (fault is "stale") { _ = await source.ObserveAsync(executable, () => true, Token); }
        if (fault is "expired-review") { clock.Now = clock.Now.AddMinutes(5); }
        if (fault is "audit") { audit.FailTerminal = true; }
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (fault is "cancelled") { await cancelled.CancelAsync(); }
        Action approve = () => source.Admit(reviewed, () => true, () => fault is not "privacy", cancelled.Token);
        approve.Should().Throw<Exception>();
        (await source.ResolveAsync(Token)).Should().BeNull();
        await File.WriteAllTextAsync(executable, "no-lock", Token);
        audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task Dictated_runtime_paths_do_not_even_become_review_candidates()
    {
        using var paths = new OwnedStorageFixture();
        using var source = new CopilotEvaluationDeploymentSource(paths, new Clock(), new Audit(),
            NullLogger<CopilotEvaluationDeploymentSource>.Instance);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice), HostActivityLayer.Windows, HostOperation.Request);
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ObserveAsync(@"Q:\not-opened.exe", () => true, Token));
        (await source.ResolveAsync(Token)).Should().BeNull();
    }

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal bool FailTerminal { get; set; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            HostActivity.RequireCurrent();
            if (FailTerminal && auditEvent.Outcome == SecurityAuditOutcome.Succeeded)
            { throw new IOException("synthetic-audit-failure"); }
            Events.Add(auditEvent);
        }
    }
}
