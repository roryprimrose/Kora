namespace Kora.Application.Diagnostics;

public sealed record GitHubIssueLookupResult(IReadOnlyList<GitHubIssueMatch> Matches);
