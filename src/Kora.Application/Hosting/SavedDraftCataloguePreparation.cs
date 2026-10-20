namespace Kora.Application.Hosting;

/// <summary>A future metadata selector, not session or navigation authority.</summary>
public sealed record SavedDraftCataloguePreparation(Guid SessionId, Func<bool> IsAdmitted);
