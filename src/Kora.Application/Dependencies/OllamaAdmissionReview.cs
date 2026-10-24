using Kora.Core.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Providers;

namespace Kora.Application.Dependencies;

/// <summary>An exact short-lived original-channel preview; it is neither qualification nor execution permission.</summary>
internal sealed record OllamaAdmissionReview(
    WorkSessionAuthorization Session, RequestOrigin Origin,
    LocalModelQualificationEvidence Evidence, LocalModelAdmissionCandidate Subject,
    DateTimeOffset ExpiresAt, Func<bool> Eligible)
{
    internal string Description => $"Review on-device model: {Evidence.Label}. Runtime {Evidence.RuntimeVersion}; "
        + $"model {Evidence.Model}; thinking off; host-bounded context. "
        + "Release qualification is verified independently. Allow this exact running instance for this host lifetime; "
        + "no installation, download, hosted fallback or automatic restart. Model and tool use still require each request's admission.";
}