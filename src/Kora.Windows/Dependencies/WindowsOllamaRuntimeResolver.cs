using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Microsoft.Extensions.Logging;

namespace Kora.Windows.Dependencies;

/// <summary>Fresh read-only observation of an independently authorized exact runtime. Never discovers a substitute.</summary>
public sealed partial class WindowsOllamaRuntimeResolver : IProviderCapabilityCatalogue
{
    private readonly IWindowsOllamaQualificationSource qualifications;
    private readonly ILocalProviderInstallationObservation installation;
    private readonly TimeProvider time;
    private readonly ILogger<WindowsOllamaRuntimeResolver> logger;
    private readonly Func<WindowsOllamaRuntimeIdentity, CancellationToken, Task<bool>> observe;

    public WindowsOllamaRuntimeResolver(IWindowsOllamaQualificationSource qualifications,
        ILocalProviderInstallationObservation installation, TimeProvider time, ILogger<WindowsOllamaRuntimeResolver> logger)
        : this(qualifications, installation, time, logger, WindowsOllamaToolProcessObservation.IsCurrentAsync) { }

    internal WindowsOllamaRuntimeResolver(IWindowsOllamaQualificationSource qualifications,
        ILocalProviderInstallationObservation installation, TimeProvider time, ILogger<WindowsOllamaRuntimeResolver> logger,
        Func<WindowsOllamaRuntimeIdentity, CancellationToken, Task<bool>> observe)
    {
        this.qualifications = qualifications;
        this.installation = installation;
        this.time = time;
        this.logger = logger;
        this.observe = observe;
    }

    public async Task<WindowsOllamaQualification?> ResolveAsync(ModelProviderSelection selection,
        ProviderProfileTuple profile, CancellationToken cancellationToken)
    {
        var receipt = await ObserveQualifiedAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return receipt is not null && ProviderProfileRules.Resolve(profile, receipt.Catalogue, time.GetUtcNow())?.Selection == selection
            ? receipt : null;
    }

    public async Task<ProviderCatalogueObservation> ObserveAsync(CancellationToken cancellationToken)
    {
        var installed = await installation.ObserveAsync(cancellationToken).ConfigureAwait(false);
        var receipt = installed == LocalProviderInstallation.Installed
            ? await ObserveQualifiedAsync(cancellationToken).ConfigureAwait(false) : null;
        cancellationToken.ThrowIfCancellationRequested();
        var now = time.GetUtcNow();
        return new(installed, new[] { ProviderService.Local, ProviderService.Copilot, ProviderService.Claude, ProviderService.Codex }
            .Select(service => service == ProviderService.Local && receipt is not null ? receipt.Catalogue
                : new ProviderCapabilityCatalogue(service, ProviderCatalogueStatus.Unavailable, null,
                    new(1), now, now.AddMinutes(1), [])).ToArray());
    }

    public async Task<WindowsOllamaToolEndpointBinding?> BindAsync(WindowsOllamaQualification expected,
        ModelTurnProvenance provenance, ProviderProfileTuple profile, Func<bool> admitted, CancellationToken revoked)
    {
        var current = await ResolveAsync(provenance.Selection, profile, revoked).ConfigureAwait(false);
        if (!ReferenceEquals(current, expected) || !admitted()) { return null; }
        var runtime = expected.Runtime;
        return new(provenance, profile, expected.Model, expected.ModelDigest, expected.RuntimeVersion,
            runtime.ProcessId, runtime.StartedAt, runtime.ExecutablePath, runtime.ExecutableSha256,
            expected.Catalogue.ValidUntil, () => expected.IsCurrent(time.GetUtcNow()) && admitted(), revoked);
    }

    private async Task<WindowsOllamaQualification?> ObserveQualifiedAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var receipt = await qualifications.ReadAsync(token).ConfigureAwait(false);
        if (receipt is null || !receipt.IsCurrent(time.GetUtcNow())
            || await installation.ObserveAsync(token).ConfigureAwait(false) != LocalProviderInstallation.Installed)
        {
            Unavailable(logger, "qualification-missing-or-stale");
            return null;
        }
        var current = await observe(receipt.Runtime, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!current || !receipt.IsCurrent(time.GetUtcNow())
            || !ReferenceEquals(receipt, await qualifications.ReadAsync(token).ConfigureAwait(false)))
        {
            Unavailable(logger, "process-identity-unconfirmed");
            return null;
        }
        token.ThrowIfCancellationRequested();
        return receipt;
    }
}
