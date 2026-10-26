using Kora.Core.Dependencies;
using Kora.Core.Network;
using Kora.Core.Tools;

namespace Kora.Application.Network;

/// <summary>Serial finite destinations, each retaining its independent canonical action authorization.</summary>
internal sealed class LocalWebPageWorkflow(IReadOnlyList<(Uri Destination, LocalWebPageInvocation Invocation)> pages)
{
    internal bool Completed => pages.All(page => page.Invocation.Completed);

    internal async Task ValidateAsync(CancellationToken token)
    {
        foreach (var page in pages) { await page.Invocation.ValidateAsync(token).ConfigureAwait(false); }
    }

    internal ValueTask<CapabilityReply> InvokeAsync(ModelTurnProvenance provenance, string id, string input, CancellationToken token)
    {
        var parsed = CapabilityInputPolicy.Read(LocalWebPageTool.Descriptor, input);
        foreach (var page in pages)
        {
            if (parsed.DenialReason is null && parsed.WebPageAddress == page.Destination)
            { return page.Invocation.InvokeAsync(provenance, id, input, token); }
        }
        return ValueTask.FromResult(new CapabilityReply(CapabilityOutcome.Denied, "frozen-page-destination-required"));
    }
}
