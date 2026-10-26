using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Windows.Dependencies;

using Microsoft.Extensions.Logging;

namespace Kora;

/// <summary>Owns one revocable exact-turn transport; no endpoint or process identity comes from the model.</summary>
internal sealed class OllamaModelTurnAdapter : IModelTurnAdapter, IModelQuestionTurnAdapter
{
    private readonly WindowsOllamaRuntimeResolver resolver;
    private readonly WindowsOllamaQualification receipt;
    private readonly TimeProvider time;
    private readonly ILoggerFactory logging;
    private readonly Func<WindowsOllamaToolEndpointBinding, IModelToolTransport> createTransport;
    private volatile bool joined = true;
    public bool IsQuiescent => joined;

    internal OllamaModelTurnAdapter(WindowsOllamaRuntimeResolver resolver, WindowsOllamaQualification receipt,
        TimeProvider time, ILoggerFactory logging,
        Func<WindowsOllamaToolEndpointBinding, IModelToolTransport>? createTransport = null)
    {
        this.resolver = resolver;
        this.receipt = receipt;
        this.time = time;
        this.logging = logging;
        this.createTransport = createTransport ?? (binding => new WindowsOllamaToolTransport(
            request => request.Provenance == binding.Provenance && request.Profile == binding.Profile ? binding : null,
            time, logging.CreateLogger<WindowsOllamaToolTransport>()));
    }

    public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
        ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Denied, provenance.Selection, Reason: ModelTurnReason.HostContextRequired));

    public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
        ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        Func<CancellationToken, ValueTask<ModelTurnReason>> validateContinuation, CancellationToken cancellationToken) =>
        RunOwnedAsync(provenance, profile, context, invokeTool, validateContinuation, null, cancellationToken);

    public Task<ModelProviderReply> RunWithQuestionAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
        ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        Func<CancellationToken, ValueTask<ModelTurnReason>> validateContinuation,
        Func<ModelClarificationQuestion, CancellationToken, Task<ModelQuestionAnswerMessage>> ask, CancellationToken cancellationToken) =>
        RunOwnedAsync(provenance, profile, context, invokeTool, validateContinuation, ask, cancellationToken);

    private async Task<ModelProviderReply> RunOwnedAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
        ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        Func<CancellationToken, ValueTask<ModelTurnReason>> validateContinuation,
        Func<ModelClarificationQuestion, CancellationToken, Task<ModelQuestionAnswerMessage>>? ask, CancellationToken token)
    {
        using var turnLifetime = CancellationTokenSource.CreateLinkedTokenSource(token, receipt.Revoked);
        bool Live() => !turnLifetime.IsCancellationRequested && receipt.IsCurrent(time.GetUtcNow())
            && HostActivity.Current is { } current && !current.Activity!.IsStopped
            && current.Outcome == HostOperationOutcome.Unknown && ReferenceEquals(current.Request, provenance.Request);
        async ValueTask<ModelTurnReason> Guard(CancellationToken cancellation)
        {
            var reason = await validateContinuation(cancellation).ConfigureAwait(false);
            return reason != ModelTurnReason.None ? reason : Live() ? ModelTurnReason.None : ModelTurnReason.QualificationPending;
        }
        if (profile is null || !Live())
        { return new(ModelTurnOutcome.Unavailable, provenance.Selection, Reason: ModelTurnReason.QualificationPending); }
        var denied = await Guard(turnLifetime.Token).ConfigureAwait(false);
        if (denied != ModelTurnReason.None)
        { return new(ModelTurnOutcome.Denied, provenance.Selection, Reason: denied); }
        var binding = await resolver.BindAsync(receipt, provenance, profile, Live, turnLifetime.Token).ConfigureAwait(false);
        if (binding is null)
        { return new(ModelTurnOutcome.Unavailable, provenance.Selection, Reason: ModelTurnReason.QualificationPending); }
        joined = false;
        BoundedModelTurnAdapter? adapter = null;
        try
        {
            adapter = new BoundedModelTurnAdapter(createTransport(binding), time,
                logging.CreateLogger<BoundedModelTurnAdapter>());
            return ask is null
                ? await adapter.RunAsync(provenance, profile, context, invokeTool, Guard, turnLifetime.Token).ConfigureAwait(false)
                : await adapter.RunWithQuestionAsync(provenance, profile, context, invokeTool, Guard, ask, turnLifetime.Token).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (adapter is not null) { await adapter.DisposeAsync().ConfigureAwait(false); }
                joined = true;
            }
            finally
            {
                // Revoke even after a final answer, so abandoned continuation data cannot outlive its host turn.
                await turnLifetime.CancelAsync().ConfigureAwait(false);
            }
        }
    }
}