using System.Globalization;

using Kora.Core.Diagnostics;
using Kora.Core.Maintenance;
using Kora.Core.Network.ExternalBrowser;

namespace Kora.Application.Diagnostics;

public sealed class GitHubExceptionReport
{
    public GitHubExceptionReport(ExceptionReportSignature signature, string version, string windowsVersion, string architecture)
    {
        Signature = signature;
        Version = SafeMetadata(version);
        WindowsVersion = SafeMetadata(windowsVersion);
        Architecture = SafeMetadata(architecture);
    }

    public ExceptionReportSignature Signature { get; }
    public string Version { get; }
    public string WindowsVersion { get; }
    public string Architecture { get; }
    public string Title => "Exception: " + Signature.ExceptionType;
    public string Body => CreateBody([]);
    public const string Disclosure = "Only the fingerprint is sent during duplicate lookup. "
        + "Opening a new issue sends the exact reviewed report in the URL to GitHub and your external browser, which may retain it in history or sync it. "
        + "A GitHub account is required to post. Review and select Create on GitHub to publish; opening the browser does not submit an issue. "
        + "Public issues can be indexed, copied or emailed; deletion cannot recall those copies. No raw logs, exception messages, conversation content or local identifiers are attached. "
        + "Method names can still identify installed components; inspect the stack before sharing. Kora cannot close the external browser or recall a handed-off report.";

    public string CreateBody(IReadOnlyList<GitHubIssueMatch> related)
    {
        var links = related.Count == 0 ? string.Empty
            : "\n## Possible regression\nPreviously reported in "
                + string.Join(", ", related.Select(match => "#" + match.Number.ToString(CultureInfo.InvariantCulture)))
                + ". Verify that this version includes the earlier fix before posting.\n";
        return "## Problem\nDescribe what you were doing and what you expected. Do not include private content or credentials.\n\n"
            + "## Environment\nKora: " + Version + "\nWindows: " + WindowsVersion + "\nArchitecture: " + Architecture
            + "\n\n## Exception\nType: " + Signature.ExceptionType + "\n\n```text\n" + Signature.StackTrace + "\n```\n\n"
            + "Kora error fingerprint: v1:" + Signature.Fingerprint + "\n" + links
            + "\nA fingerprint identifies a normalised exception signature, not a proven unique root cause.\n";
    }

    public ExternalBrowserDestination NewIssueDestination(IReadOnlyList<GitHubIssueMatch> related) =>
        ExternalBrowserDestination.Validate(CanonicalRelease.RepositoryUrl + "/issues/new?title="
            + Uri.EscapeDataString(Title) + "&body=" + Uri.EscapeDataString(CreateBody(related)))
        ?? throw new InvalidDataException("This report exceeds the browser URL limit. No stack was truncated and nothing was opened.");

    private static string SafeMetadata(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128
            || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '+' or ' ' or '_')))
        {
            throw new InvalidDataException("Report environment metadata is unavailable or malformed.");
        }
        return value;
    }
}
