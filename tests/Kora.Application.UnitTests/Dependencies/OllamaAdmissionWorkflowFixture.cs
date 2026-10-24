using System.Security.Cryptography;
using System.Text.Json;

using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Providers;

using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Dependencies;

internal sealed class OllamaAdmissionWorkflowFixture : ILocalModelQualificationAuthority, ILocalModelAdmissionBroker, IPreferenceStore,
    ILogger<OllamaAdmissionWorkflow>, IAsyncDisposable
{
    private static readonly string[] Gates = ["licence-identity", "quality-authority", "bounds", "cancellation-recovery",
        "resource-latency", "offline-egress", "integrated-host"];
    internal ProviderProfileConfigurationFixture Host { get; } = new();
    internal Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
    internal OllamaAdmissionWorkflow Workflow { get; }
    internal LocalModelQualificationEvidence Evidence { get; }
    internal IReadOnlyList<LocalModelQualificationEvidence> Trusted { get; set; }
    internal LocalModelAdmissionCandidate Candidate { get; }
    internal bool Available { get; set; } = true;
    internal bool Current { get; set; } = true;
    internal Func<bool>? Admitted { get; private set; }
    internal string? FailWrite { get; set; }
    internal string? FailDelete { get; set; }
    internal Action? AfterSave { get; set; }
    internal Action? AfterConfirm { get; set; }
    internal Action? AfterCurrent { get; set; }
    internal Action? AfterRead { get; set; }
    internal Func<CancellationToken, Task>? BeforeObserve { get; set; }
    internal int Observations { get; private set; }
    internal List<(LogLevel Level, EventId EventId, string Message)> Diagnostics { get; } = [];
    internal OllamaAdmissionWorkflowFixture()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Format = 1,
            HostContract = LocalModelQualificationEvidence.HostContract,
            ProfileId = Guid.NewGuid(),
            Revision = 1,
            Label = "Deterministic fixture only",
            RuntimeVersion = "fixture",
            ExecutableSha256 = new string('a', 64),
            Model = "fixture:one",
            ModelSha256 = new string('a', 64),
            Reasoning = "off",
            Context = "bounded",
            IssuedAt = Host.Time.Now,
            ExpiresAt = Host.Time.Now.AddHours(1),
            Proofs = Gates.Select(gate => new
            {
                Gate = gate,
                SourceRevision = new string('b', 40),
                RunId = "fixture-only",
                ArtifactSha256 = new string('a', 64),
                Outcome = "passed"
            }).ToArray(),
        });
        Evidence = LocalModelQualificationEvidence.Read(bytes, Convert.ToHexString(SHA256.HashData(bytes)));
        Trusted = [Evidence];
        Candidate = new(Guid.NewGuid(), Evidence.Digest, new("fixture-account"));
        Workflow = NewWorkflow();
    }

    internal OllamaAdmissionWorkflow NewWorkflow() => new(this, this, new(this), Host.Admission, Host.Audit,
        Host.Time, this);
    internal Task<OllamaAdmissionReview?> Review() => Workflow.Review(Kora.Core.Hosting.RequestOrigin.LocalUi,
        () => Host.Eligible, Host.Token);
    internal Task Confirm(OllamaAdmissionReview review) => Workflow.Confirm(review, Kora.Core.Hosting.RequestOrigin.LocalUi,
        () => Host.Eligible, Host.Token);
    internal Task RevokeAdmission() => Workflow.Revoke(Kora.Core.Hosting.RequestOrigin.LocalUi, () => Host.Eligible, Host.Token);
    public Task<IReadOnlyList<LocalModelQualificationEvidence>> Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AfterRead?.Invoke();
        return Task.FromResult(Trusted);
    }
    public async Task<LocalModelAdmissionCandidate?> Observe(LocalModelQualificationEvidence qualification, CancellationToken cancellationToken)
    {
        if (BeforeObserve is { } wait) { await wait(cancellationToken).ConfigureAwait(false); }
        cancellationToken.ThrowIfCancellationRequested();
        Observations++;
        return Available ? Candidate : null;
    }
    public Task<bool> IsCurrent(LocalModelQualificationEvidence qualification, LocalModelAdmissionCandidate expected, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AfterCurrent?.Invoke();
        return Task.FromResult(Current);
    }
    public void Admit(LocalModelQualificationEvidence qualification, LocalModelAdmissionCandidate expected, Func<bool> eligible, CancellationToken revoked) =>
        Admitted = () => !revoked.IsCancellationRequested && eligible();
    public void Revoke() => Admitted = null;
    public string? ReadText(string fileName) => Values.GetValueOrDefault(fileName);
    public string[]? ReadLines(string fileName) => throw new NotSupportedException();
    public void WriteLines(string fileName, IEnumerable<string> contents) => throw new NotSupportedException();
    public void WriteText(string fileName, string contents)
    {
        if (string.Equals(fileName, FailWrite, StringComparison.Ordinal)) { throw new IOException("Fixture write failure."); }
        Values[fileName] = contents;
        if (string.Equals(fileName, "local-model-admission.json", StringComparison.Ordinal)) { AfterSave?.Invoke(); }
    }
    public void Delete(string fileName)
    {
        if (string.Equals(fileName, FailDelete, StringComparison.Ordinal)) { throw new IOException("Fixture delete failure."); }
        Values.Remove(fileName);
        if (string.Equals(fileName, "local-model-admission-unconfirmed.txt", StringComparison.Ordinal)) { AfterConfirm?.Invoke(); }
    }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Diagnostics.Add((logLevel, eventId, formatter(state, exception)));
    public async ValueTask DisposeAsync() { await Workflow.DisposeAsync(); await Host.DisposeAsync(); }
}