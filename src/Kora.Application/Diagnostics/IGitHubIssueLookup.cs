namespace Kora.Application.Diagnostics;

public interface IGitHubIssueLookup
{
    Task<GitHubIssueLookupResult> FindAsync(string fingerprint, Func<bool> admitted, CancellationToken cancellationToken);
}
