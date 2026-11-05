using System.Globalization;

using Kora.Core.Maintenance;

namespace Kora.Application.Diagnostics;

public sealed record GitHubIssueMatch(int Number, string Title, bool Closed)
{
    public Uri Address => new(CanonicalRelease.RepositoryUrl + "/issues/" + Number.ToString(CultureInfo.InvariantCulture));
}
