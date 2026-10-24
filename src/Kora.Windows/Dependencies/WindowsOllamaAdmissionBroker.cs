using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Providers;

using Microsoft.Extensions.Logging;

namespace Kora.Windows.Dependencies;

/// <summary>Native receipt producer and qualification source. No process/session observation is persisted.</summary>
public sealed partial class WindowsOllamaAdmissionBroker(
    ILocalModelQualificationAuthority authority, IWindowsOllamaAdmissionObservation observation, TimeProvider time,
    ILogger<WindowsOllamaAdmissionBroker> logger)
    : ILocalModelAdmissionBroker, IWindowsOllamaQualificationSource, IDisposable
{
    private readonly Lock gate = new();
    private LocalModelAdmissionCandidate? candidate;
    private WindowsOllamaAdmissionSubject? subject;
    private LocalModelQualificationEvidence? evidence;
    private WindowsOllamaQualification? receipt;
    private CancellationTokenSource? revocation;
    private Func<bool>? admitted;
    private bool disposed;
    private long revision;

    public async Task<LocalModelAdmissionCandidate?> Observe(LocalModelQualificationEvidence qualification, CancellationToken cancellationToken)
    {
        var trusted = await Trusted(qualification, cancellationToken).ConfigureAwait(false);
        if (!trusted) { return null; }
        long expected;
        lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); expected = revision; }
        var current = await observation.Observe(qualification, cancellationToken).ConfigureAwait(false);
        if (!await Trusted(qualification, cancellationToken).ConfigureAwait(false)) { return null; }
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (disposed || revision != expected || current is null || !qualification.IsCurrent(time.GetUtcNow())) { return null; }
            CloseReceipt();
            revision++;
            candidate = new(Guid.NewGuid(), qualification.Digest, current.Account);
            subject = current;
            evidence = qualification;
            return candidate;
        }
    }

    public async Task<bool> IsCurrent(LocalModelQualificationEvidence qualification, LocalModelAdmissionCandidate expected,
        CancellationToken cancellationToken)
    {
        WindowsOllamaAdmissionSubject? captured;
        lock (gate)
        {
            if (disposed || !ReferenceEquals(candidate, expected)
                || !string.Equals(evidence?.Digest, qualification.Digest, StringComparison.Ordinal)) { return false; }
            captured = subject;
        }
        if (captured is null || !await Trusted(qualification, cancellationToken).ConfigureAwait(false)
            || !await observation.IsCurrent(qualification, captured, cancellationToken).ConfigureAwait(false)
            || !await Trusted(qualification, cancellationToken).ConfigureAwait(false))
        { return false; }
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return !disposed && ReferenceEquals(candidate, expected) && ReferenceEquals(subject, captured)
            && qualification.IsCurrent(time.GetUtcNow());
        }
    }

    public void Admit(LocalModelQualificationEvidence qualification, LocalModelAdmissionCandidate expected, Func<bool> eligible,
        CancellationToken revoked)
    {
        var current = HostActivity.RequireCurrent();
        lock (gate)
        {
            if (disposed || !ReferenceEquals(expected, candidate)
                || !string.Equals(evidence?.Digest, qualification.Digest, StringComparison.Ordinal)
                || subject is null || !qualification.IsCurrent(time.GetUtcNow()) || !eligible() || revoked.IsCancellationRequested
                || current.Activity!.IsStopped || current.Outcome != HostOperationOutcome.Unknown
                || current.Request.Origin is not (Kora.Core.Hosting.RequestOrigin.LocalUi or Kora.Core.Hosting.RequestOrigin.ActivatedVoice))
            { throw new InvalidOperationException("Current original-user admission and exact qualified native observation are required."); }
            CloseReceipt();
            revocation = CancellationTokenSource.CreateLinkedTokenSource(revoked);
            admitted = eligible;
            var profile = new ProviderProfileTuple(ProviderService.Local, expected.Account, new(qualification.Revision),
                new(qualification.ProfileId.ToString("D")), new("off"), new("bounded"));
            var model = new ProviderCatalogueModel(profile.Model, qualification.Label,
                new(ModelProviderIdentity.Ollama, new(qualification.ProfileId), profile.CatalogueRevision),
                [new(profile.Reasoning, "Off", false)], [new(profile.Context, "Host bounded", true)]);
            receipt = new(Guid.NewGuid(), new(ProviderService.Local, ProviderCatalogueStatus.Current, expected.Account,
                profile.CatalogueRevision, time.GetUtcNow(), qualification.ExpiresAt, [model]), subject.Runtime,
                qualification.RuntimeVersion, qualification.Model, qualification.ModelSha256, revocation.Token, qualification);
            revision++;
        }
    }

    public async Task<WindowsOllamaQualification?> ReadAsync(CancellationToken cancellationToken)
    {
        WindowsOllamaQualification? captured;
        LocalModelAdmissionCandidate? expected;
        LocalModelQualificationEvidence? qualification;
        lock (gate)
        {
            if (disposed || receipt is null) { return null; }
            captured = receipt;
            expected = candidate;
            qualification = evidence;
        }
        try
        {
            using var deadline = new CancellationTokenSource(ModelToolProtocol.Deadline, time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            cancellationToken = linked.Token;
            if (expected is null || qualification is null
                || !await IsCurrent(qualification, expected, cancellationToken).ConfigureAwait(false))
            { ReceiptUnavailable(logger, "qualification-or-subject-changed"); RevokeCaptured(captured); return null; }
            lock (gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (disposed || !ReferenceEquals(receipt, captured)) { return null; }
                if (!captured.IsCurrent(time.GetUtcNow()) || admitted?.Invoke() != true)
                { ReceiptUnavailable(logger, "admission-expired-or-changed"); Revoke(); return null; }
                return captured;
            }
        }
        catch (Exception exception) { ReceiptUnavailable(logger, "receipt-read-failed", exception); RevokeCaptured(captured); throw; }
    }

    private void RevokeCaptured(WindowsOllamaQualification captured)
    {
        lock (gate) { if (ReferenceEquals(receipt, captured)) { Revoke(); } }
    }

    private async Task<bool> Trusted(LocalModelQualificationEvidence qualification, CancellationToken cancellationToken)
    {
        var accepted = await authority.Read(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return qualification.IsCurrent(time.GetUtcNow()) && accepted.Any(item =>
            string.Equals(item.Digest, qualification.Digest, StringComparison.Ordinal) && item.IsCurrent(time.GetUtcNow()));
    }

    public void Revoke()
    {
        lock (gate)
        {
            revision++;
            candidate = null;
            subject = null;
            evidence = null;
            CloseReceipt();
        }
    }

    private void CloseReceipt()
    {
        receipt = null;
        admitted = null;
        var previous = revocation;
        revocation = null;
        try { previous?.Cancel(); }
        finally { previous?.Dispose(); }
    }

    public void Dispose()
    {
        lock (gate) { disposed = true; Revoke(); }
        GC.SuppressFinalize(this);
    }
}