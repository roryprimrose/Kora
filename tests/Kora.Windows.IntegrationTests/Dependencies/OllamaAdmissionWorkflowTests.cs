using System.Security.Cryptography;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Voice;
using Kora.Application.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Windows.Dependencies;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Dependencies;

[Collection("Host tracing")]
public sealed class OllamaAdmissionWorkflowTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly string[] Gates = ["licence-identity", "quality-authority", "bounds", "cancellation-recovery",
        "resource-latency", "offline-egress", "integrated-host"];
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task HostProducesOnlyReviewedFreshQualifiedReceiptAndPersistsNoProcessAuthority(RequestOrigin origin)
    {
        await using var f = await Fixture.Create();
        var review = await f.Workflow.Review(origin, () => f.Eligible, Token);
        review.Should().NotBeNull();
        review!.Description.Should().Contain("thinking off").And.Contain("no installation");
        (await f.Broker.ReadAsync(Token)).Should().BeNull("review alone is not consent or qualification");
        await f.Workflow.Confirm(review, origin, () => f.Eligible, Token);
        var receipt = await f.Broker.ReadAsync(Token);
        receipt.Should().NotBeNull();
        receipt!.RuntimeVersion.Should().Be("fake-runtime");
        receipt.Runtime.Should().Be(f.Subject.Runtime);
        receipt.Catalogue.Account.Should().Be(f.Subject.Account);
        receipt.Catalogue.Models.Single().Selection.Model.Value.Should().Be(f.Evidence.ProfileId);
        receipt.IsCurrent(f.Storage.Time.Now).Should().BeTrue();
        f.Audits.Where(item => string.Equals(item.ActionId, "model.local-admission", StringComparison.Ordinal)).Select(item => item.Outcome)
            .Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Store.Values["local-model-admission.json"].Should().NotContain("4242").And.NotContain("ollama.exe");
        f.Store.Values.Keys.Should().NotContain("local-model-admission-unconfirmed.txt");
        var resolver = new WindowsOllamaRuntimeResolver(f.Broker, new Installed(), f.Storage.Time,
            NullLogger<WindowsOllamaRuntimeResolver>.Instance, (_, _) => Task.FromResult(true));
        var catalogue = (await resolver.ObserveAsync(Token)).For(ProviderService.Local);
        catalogue.Status.Should().Be(ProviderCatalogueStatus.Current);
        var model = catalogue.Models.Single();
        var profile = new ProviderProfileTuple(ProviderService.Local, catalogue.Account!, catalogue.Revision,
            model.Id, model.Reasoning.Single().Id, model.Context.Single().Id);
        (await resolver.ResolveAsync(model.Selection, profile, Token)).Should().BeSameAs(receipt);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), HostRequest.Create(origin), model.Selection);
        using var revoked = CancellationTokenSource.CreateLinkedTokenSource(receipt.Revoked);
        var binding = await resolver.BindAsync(receipt, provenance, profile, () => f.Eligible, revoked.Token);
        binding.Should().NotBeNull();
        await f.Workflow.Revoke(origin, () => f.Eligible, Token);
        receipt.Revoked.IsCancellationRequested.Should().BeTrue();
        binding!.Revoked.IsCancellationRequested.Should().BeTrue();
        (await f.Broker.ReadAsync(Token)).Should().BeNull();
        f.Store.Values.Should().BeEmpty();
        (await resolver.ObserveAsync(Token)).For(ProviderService.Local).Models.Should().BeEmpty();
    }

    [Theory]
    [InlineData("no-evidence")]
    [InlineData("expired")]
    [InlineData("not-yet-valid")]
    [InlineData("ambiguous")]
    [InlineData("runtime-missing")]
    public async Task MissingEvidenceOrSubjectNeverIssuesAReviewOrQualification(string fault)
    {
        await using var f = await Fixture.Create();
        if (fault is "no-evidence") { f.Trusted = []; }
        if (fault is "expired") { f.Storage.Time.Now = f.Storage.Time.Now.AddHours(2); }
        if (fault is "not-yet-valid") { f.Storage.Time.Now = f.Storage.Time.Now.AddHours(-2); }
        if (fault is "ambiguous") { f.Trusted = [f.Evidence, f.Evidence]; }
        if (fault is "runtime-missing") { f.SubjectAvailable = false; }
        (await f.Workflow.Review(RequestOrigin.LocalUi, () => f.Eligible, Token)).Should().BeNull();
        f.Workflow.Status.Should().Contain("Unavailable:");
        f.Observations.Should().Be(fault is "runtime-missing" ? 1 : 0);
        f.Store.Values.Should().BeEmpty();
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("replay")]
    [InlineData("channel")]
    [InlineData("review-expired")]
    [InlineData("evidence-removed")]
    [InlineData("subject-changed")]
    [InlineData("host-lost")]
    public async Task ExactReviewCannotBeReplayedOrPromoteChangedAuthority(string fault)
    {
        await using var f = await Fixture.Create();
        var review = (await f.Workflow.Review(RequestOrigin.LocalUi, () => f.Eligible, Token))!;
        if (fault is "foreign") { review = review with { }; }
        if (fault is "review-expired") { f.Storage.Time.Now = f.Storage.Time.Now.AddSeconds(31); }
        if (fault is "evidence-removed") { f.Trusted = []; }
        if (fault is "subject-changed") { f.SubjectAvailable = false; }
        if (fault is "host-lost") { f.Eligible = false; }
        if (fault is "replay") { await f.Workflow.Confirm(review, RequestOrigin.LocalUi, () => f.Eligible, Token); }
        await f.Workflow.Invoking(value => value.Confirm(review,
            fault is "channel" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi, () => f.Eligible, Token))
            .Should().ThrowAsync<Exception>();
        (await f.Broker.ReadAsync(Token)).Should().BeNull();
    }

    [Theory]
    [InlineData("process-exit")]
    [InlineData("host-loss")]
    [InlineData("removed-evidence")]
    [InlineData("expiry")]
    [InlineData("choice-changed")]
    [InlineData("changed-during-observation")]
    public async Task CurrentReceiptIsRevokedOnAnyEvidenceOwnershipOrPersistenceLoss(string fault)
    {
        await using var f = await Fixture.Create();
        var review = (await f.Workflow.Review(RequestOrigin.LocalUi, () => f.Eligible, Token))!;
        await f.Workflow.Confirm(review, RequestOrigin.LocalUi, () => f.Eligible, Token);
        var receipt = (await f.Broker.ReadAsync(Token))!;
        if (fault is "process-exit") { f.SubjectAvailable = false; }
        if (fault is "host-loss") { f.Eligible = false; }
        if (fault is "removed-evidence") { f.Trusted = []; }
        if (fault is "expiry") { f.Storage.Time.Now = f.Storage.Time.Now.AddHours(2); }
        if (fault is "choice-changed") { f.Store.Values.Remove("local-model-admission.json"); }
        if (fault is "changed-during-observation") { f.OnCurrent = () => f.Store.Values.Remove("local-model-admission.json"); }
        (await f.Broker.ReadAsync(Token)).Should().BeNull();
        receipt.Revoked.IsCancellationRequested.Should().BeTrue();
        (await f.Broker.ReadAsync(Token)).Should().BeNull("lost authority cannot select a replacement");
    }

    [Theory]
    [InlineData("write-failed")]
    [InlineData("audit-failed")]
    [InlineData("confirm-audit-host-loss")]
    [InlineData("readback-mismatch")]
    public async Task PersistenceOrRequiredAuditFailureCannotPublishReceiptAndRequiresExplicitRecovery(string fault)
    {
        await using var f = await Fixture.Create();
        var review = (await f.Workflow.Review(RequestOrigin.LocalUi, () => f.Eligible, Token))!;
        if (fault is "write-failed") { f.Store.FailWrite = "local-model-admission.json"; }
        if (fault is "audit-failed") { f.FailAdmissionAudit = true; }
        if (fault is "confirm-audit-host-loss") { f.OnSuccessfulAudit = () => f.Eligible = false; }
        if (fault is "readback-mismatch") { f.Store.OnSave = () => f.Store.Values.Remove("local-model-admission.json"); }
        await f.Workflow.Invoking(value => value.Confirm(review, RequestOrigin.LocalUi, () => f.Eligible, Token))
            .Should().ThrowAsync<Exception>();
        f.Store.Values.Keys.Should().Contain("local-model-admission-unconfirmed.txt");
        f.Eligible = true;
        await f.Workflow.Invoking(value => value.Review(RequestOrigin.LocalUi, () => f.Eligible, Token)).Should().ThrowAsync<InvalidDataException>();
        f.FailAdmissionAudit = false;
        f.Eligible = true;
        f.OnSuccessfulAudit = null;
        f.Store.FailWrite = null;
        f.Store.OnSave = null;
        await f.Workflow.Revoke(RequestOrigin.LocalUi, () => f.Eligible, Token);
        (await f.Broker.ReadAsync(Token)).Should().BeNull();
        f.Store.Values.Should().BeEmpty();
    }

    [Fact]
    public async Task RestartRetainsOnlyReviewChoiceAndDisposalRevokesLiveReceipt()
    {
        await using var f = await Fixture.Create();
        var review = (await f.Workflow.Review(RequestOrigin.LocalUi, () => f.Eligible, Token))!;
        await f.Workflow.Confirm(review, RequestOrigin.LocalUi, () => f.Eligible, Token);
        var receipt = (await f.Broker.ReadAsync(Token))!;
        await f.Workflow.DisposeAsync();
        receipt.Revoked.IsCancellationRequested.Should().BeTrue();
        await using var restarted = f.NewWorkflow();
        (await f.Broker.ReadAsync(Token)).Should().BeNull();
        f.Store.Values.Keys.Should().Contain("local-model-admission.json");
    }

    [Fact]
    public async Task MalformedSavedChoiceAndCancelledReviewAreExplicitAndDoNotObserveARuntime()
    {
        await using var f = await Fixture.Create();
        f.Store.Values["local-model-admission.json"] = "{";
        await f.Workflow.Invoking(value => value.Review(RequestOrigin.LocalUi, () => true, Token))
            .Should().ThrowAsync<InvalidDataException>();
        f.Observations.Should().Be(0);
        f.Store.Values.Clear();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await f.Workflow.Invoking(value => value.Review(RequestOrigin.LocalUi, () => true, cancelled.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        f.Observations.Should().Be(0);
    }

    [Fact]
    public async Task CopiedCandidateAndUncorrelatedDirectAdmissionCannotPublishAuthority()
    {
        await using var f = await Fixture.Create();
        var review = (await f.Workflow.Review(RequestOrigin.LocalUi, () => true, Token))!;
        (await f.Broker.IsCurrent(f.Evidence, review.Subject with { }, Token)).Should().BeFalse();
        f.Broker.Invoking(value => value.Admit(f.Evidence, review.Subject, () => true, Token))
            .Should().Throw<InvalidOperationException>();
        (await f.Broker.ReadAsync(Token)).Should().BeNull();
    }

    [Fact]
    public async Task EvidenceRemovedDuringNativeRevalidationRevokesPreviouslyAdmittedReceipt()
    {
        await using var f = await Fixture.Create();
        var review = (await f.Workflow.Review(RequestOrigin.LocalUi, () => true, Token))!;
        await f.Workflow.Confirm(review, RequestOrigin.LocalUi, () => true, Token);
        var receipt = (await f.Broker.ReadAsync(Token))!;
        f.OnCurrent = () => f.Trusted = [];
        (await f.Broker.ReadAsync(Token)).Should().BeNull();
        receipt.Revoked.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task OldReadCannotRevokeASeparatelyReviewedNewReceiptAfterObservationRace()
    {
        await using var f = await Fixture.Create();
        var review = (await f.Workflow.Review(RequestOrigin.LocalUi, () => true, Token))!;
        await f.Workflow.Confirm(review, RequestOrigin.LocalUi, () => true, Token);
        var old = (await f.Broker.ReadAsync(Token))!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = true;
        f.BeforeCurrent = async () =>
        {
            if (!first) { return; }
            first = false;
            entered.SetResult();
            await released.Task.WaitAsync(Token);
        };
        var reading = f.Broker.ReadAsync(Token);
        await Task.WhenAny(entered.Task, reading).WaitAsync(Token);
        reading.IsCompleted.Should().BeFalse();
        try
        {
            var replacement = (await f.Workflow.Review(RequestOrigin.LocalUi, () => true, Token))!;
            old.Revoked.IsCancellationRequested.Should().BeTrue();
            await f.Workflow.Confirm(replacement, RequestOrigin.LocalUi, () => true, Token);
        }
        finally { released.TrySetResult(); }
        (await reading).Should().BeNull();
        (await f.Broker.ReadAsync(Token)).Should().NotBeNull("an old source read cannot revoke a newly admitted receipt");
    }

    private sealed class Installed : ILocalProviderInstallationObservation
    {
        public Task<LocalProviderInstallation> ObserveAsync(CancellationToken cancellationToken) => Task.FromResult(LocalProviderInstallation.Installed);
    }

    internal sealed class Fixture : ILocalModelQualificationAuthority, IWindowsOllamaAdmissionObservation, ISecurityAuditLog, IAsyncDisposable
    {
        internal InteractionStorageFixture Storage { get; } = new();
        internal MemoryPreferences Store { get; } = new();
        internal AudioControlAdmission Admission { get; private set; } = null!;
        internal OllamaAdmissionWorkflow Workflow { get; private set; } = null!;
        internal WindowsOllamaAdmissionBroker Broker { get; private set; } = null!;
        internal LocalModelQualificationEvidence Evidence { get; private set; } = null!;
        internal IReadOnlyList<LocalModelQualificationEvidence> Trusted = [];
        internal WindowsOllamaAdmissionSubject Subject { get; private set; } = null!;
        internal bool Eligible = true;
        internal bool SubjectAvailable = true;
        internal bool FailAdmissionAudit;
        internal int Observations;
        internal Action? OnCurrent;
        internal Func<Task>? BeforeCurrent;
        internal Action? OnSuccessfulAudit;
        internal List<SecurityAuditEvent> Audits { get; } = [];
        internal static async Task<Fixture> Create()
        {
            var f = new Fixture();
            await f.Storage.InitializeAsync();
            f.Admission = new(f.Storage.Store, f.Storage.Store, new(f.Storage.Tasks));
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                Format = 1,
                HostContract = LocalModelQualificationEvidence.HostContract,
                ProfileId = Guid.NewGuid(),
                Revision = 1,
                Label = "Test fixture only",
                RuntimeVersion = "fake-runtime",
                ExecutableSha256 = Digest,
                Model = "deterministic-fake:one",
                ModelSha256 = Digest,
                Reasoning = "off",
                Context = "bounded",
                IssuedAt = f.Storage.Time.Now,
                ExpiresAt = f.Storage.Time.Now.AddHours(1),
                Proofs = Gates.Select(gate => new
                {
                    Gate = gate,
                    SourceRevision = new string('b', 40),
                    RunId = "deterministic-fixture",
                    ArtifactSha256 = Digest,
                    Outcome = "passed"
                }).ToArray(),
            });
            f.Evidence = LocalModelQualificationEvidence.Read(bytes, Convert.ToHexString(SHA256.HashData(bytes)));
            f.Trusted = [f.Evidence];
            f.Subject = new(new(4242, f.Storage.Time.Now, @"C:\fake\ollama.exe", Digest), new("fake-account"), 1);
            f.Broker = new(f, f, f.Storage.Time, NullLogger<WindowsOllamaAdmissionBroker>.Instance);
            f.Workflow = f.NewWorkflow();
            return f;
        }
        internal OllamaAdmissionWorkflow NewWorkflow() => new(this, Broker, new(Store), Admission, this, Storage.Time,
            NullLogger<OllamaAdmissionWorkflow>.Instance);
        public Task<IReadOnlyList<LocalModelQualificationEvidence>> Read(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Trusted);
        }
        public Task<WindowsOllamaAdmissionSubject?> Observe(LocalModelQualificationEvidence evidence, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Observations++;
            return Task.FromResult(SubjectAvailable ? Subject : null);
        }
        public async Task<bool> IsCurrent(LocalModelQualificationEvidence evidence, WindowsOllamaAdmissionSubject subject, CancellationToken cancellationToken)
        {
            if (BeforeCurrent is { } wait) { await wait().ConfigureAwait(false); }
            cancellationToken.ThrowIfCancellationRequested();
            OnCurrent?.Invoke();
            return SubjectAvailable && subject == Subject;
        }
        public void Write(SecurityAuditEvent auditEvent)
        {
            HostActivity.RequireCurrent();
            Audits.Add(auditEvent);
            if (string.Equals(auditEvent.ActionId, "model.local-admission", StringComparison.Ordinal) && auditEvent.Outcome == SecurityAuditOutcome.Succeeded)
            {
                if (FailAdmissionAudit) { throw new IOException("Audit unavailable."); }
                OnSuccessfulAudit?.Invoke();
            }
        }
        public async ValueTask DisposeAsync()
        {
            await Workflow.DisposeAsync();
            Broker.Dispose();
            await Admission.DisposeAsync();
            Storage.Dispose();
        }
    }

    internal sealed class MemoryPreferences : IPreferenceStore
    {
        internal Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        internal string? FailWrite;
        internal Action? OnSave;
        public string? ReadText(string fileName) => Values.GetValueOrDefault(fileName);
        public string[]? ReadLines(string fileName) => throw new NotSupportedException();
        public void WriteText(string fileName, string contents)
        {
            if (string.Equals(fileName, FailWrite, StringComparison.Ordinal)) { throw new IOException("Storage unavailable."); }
            Values[fileName] = contents;
            if (string.Equals(fileName, "local-model-admission.json", StringComparison.Ordinal)) { OnSave?.Invoke(); }
        }
        public void WriteLines(string fileName, IEnumerable<string> contents) => throw new NotSupportedException();
        public void Delete(string fileName) => Values.Remove(fileName);
    }
}