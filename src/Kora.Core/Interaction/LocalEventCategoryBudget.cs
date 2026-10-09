namespace Kora.Core.Interaction;

public sealed record LocalEventCategoryBudget(LocalEventCategory Category, DateTimeOffset WindowStart, int Count, DateTimeOffset? LastPresentation);
