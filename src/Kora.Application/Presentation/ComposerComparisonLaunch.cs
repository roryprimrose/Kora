namespace Kora.Application.Presentation;

public sealed record ComposerComparisonLaunch(Func<bool> IsCurrent, Func<Task<bool>> Open);
