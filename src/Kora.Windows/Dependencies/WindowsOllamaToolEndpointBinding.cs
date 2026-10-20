using Kora.Core.Dependencies;
using Kora.Core.Providers;

namespace Kora.Windows.Dependencies;

/// <summary>
/// Host-supplied observation target, not qualification or permission. No endpoint is accepted from model data.
/// The host fence must cover current session, generation, control, privacy and exact-profile evidence.
/// Revoke the binding when that turn ends so idle continuation data is released without another provider request.
/// </summary>
public sealed class WindowsOllamaToolEndpointBinding
{
    public WindowsOllamaToolEndpointBinding(ModelTurnProvenance provenance, ProviderProfileTuple profile,
        string model, string modelDigest, string runtimeVersion, int processId, DateTimeOffset processStartedAt,
        string executablePath, string executableSha256, DateTimeOffset expiresAt,
        Func<bool> isAdmitted, CancellationToken revoked)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ProviderProfileRules.ValidateTuple(profile);
        ModelToolProtocol.ValidateProvenance(provenance);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(isAdmitted);
        if (profile.Service != ProviderService.Local || profile.CatalogueRevision != provenance.Selection.Revision)
        { throw new ArgumentException("The exact local profile must match the admitted catalogue revision.", nameof(profile)); }
        if (provenance.Selection.Provider != ModelProviderIdentity.Ollama)
        { throw new ArgumentException("An admitted local provider is required.", nameof(provenance)); }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        if (!Path.IsPathFullyQualified(executablePath) || executablePath.StartsWith(@"\\", StringComparison.Ordinal))
        { throw new ArgumentException("An absolute local executable path is required.", nameof(executablePath)); }
        if (!IsDigest(executableSha256))
        { throw new ArgumentException("The exact executable SHA-256 is required.", nameof(executableSha256)); }
        if (!IsDigest(modelDigest))
        { throw new ArgumentException("The exact model SHA-256 is required.", nameof(modelDigest)); }
        Provenance = provenance;
        Profile = profile;
        Model = model;
        ModelDigest = modelDigest;
        RuntimeVersion = runtimeVersion;
        ProcessId = processId;
        ProcessStartedAt = processStartedAt;
        ExecutablePath = executablePath;
        ExecutableSha256 = executableSha256;
        ExpiresAt = expiresAt;
        IsAdmitted = isAdmitted;
        Revoked = revoked;
    }

    public ModelTurnProvenance Provenance { get; }
    public ProviderProfileTuple Profile { get; }
    public string Model { get; }
    public string ModelDigest { get; }
    public string RuntimeVersion { get; }
    public int ProcessId { get; }
    public DateTimeOffset ProcessStartedAt { get; }
    public string ExecutablePath { get; }
    public string ExecutableSha256 { get; }
    public DateTimeOffset ExpiresAt { get; }
    public Func<bool> IsAdmitted { get; }
    public CancellationToken Revoked { get; }

    internal static bool IsDigest(string? value) =>
        value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
}
