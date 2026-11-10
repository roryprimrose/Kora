using Kora.Core.Presentation;

namespace Kora;

public sealed partial class DetailWindowController
{
    internal async Task ReopenHostDocumentAsync(SessionContentTab tab, ResponseWindow owner, Func<bool> admitted)
    {
        if (disposed || !admitted() || !canAccess() || viewModel is null)
        { throw new InvalidOperationException("The exact host document source or private presentation is no longer admitted."); }
        if (tab.Origin == DetailContentOrigin.FinalizedResponse)
        {
            await viewModel.OpenExactCurrentResponseDetailsAsync(tab.Reference, admitted);
            return;
        }
        if (tab.Origin == DetailContentOrigin.EmbeddedDocument)
        {
            var exact = pages.FirstOrDefault(entry => entry.Value.Id == tab.Reference.ItemId
                && entry.Value.Revision == tab.Reference.Revision);
            var page = documentation.GetPages().FirstOrDefault(page => string.Equals(page.Id, exact.Key, StringComparison.Ordinal))
                ?? throw new InvalidOperationException("The exact embedded document is no longer available.");
            var content = Admit(page);
            if (content.Reference != tab.Reference || !admitted())
            { throw new InvalidDataException("The exact embedded document revision changed; no newer page was substituted."); }
            _ = OpenContent(content, owner);
            return;
        }
        if (tab.Origin == DetailContentOrigin.RetrievedWebResult)
        {
            if (webResultSource(tab.Reference) is not { } content || !admitted())
            { throw new InvalidOperationException("The exact external observation has been retired; no new retrieval was attempted."); }
            _ = OpenContent(content, owner);
            return;
        }
        throw new InvalidOperationException("This exact document origin has no host reopening route.");
    }
}
