using System.Net;
using System.Net.Sockets;
using System.Xml;

namespace ContainmentProof;

internal sealed record NetworkImage(string Name, string Path, string Sha256);
internal sealed record NetworkRequest(string RequestId, string Profile, string Scratch,
    string UserSid, string? ContainerSid, DateTimeOffset StartedUtc, DateTimeOffset EndedUtc,
    string Address, int Port, NetworkImage[] Images);
internal sealed record NetworkCompletion(string RequestId, string RequestSha256, string Status,
    string Attribution, string? Error = null);

internal enum NetworkObserverMode { Strict, UserFilterComparison }
internal enum NetworkQueryScope { Strict, ApplicationOnlyDiagnostic }

internal static class NetworkCollection
{
    internal static readonly string[] Profiles =
        ["job-only-complete", "appcontainer-complete", "appcontainer-cancel"];
    internal static readonly TimeSpan HostWaitLimit = TimeSpan.FromSeconds(90);
    internal static readonly TimeSpan RequestAgeLimit = TimeSpan.FromSeconds(120);
    internal static readonly TimeSpan ComparisonQueryBudget = TimeSpan.FromSeconds(60);
    internal const int MaximumJsonBytes = 64 * 1024;
    internal const int MaximumXmlBytes = 1024 * 1024;

    internal static void Validate(NetworkRequest request, string evidenceRoot, DateTimeOffset now)
    {
        if (!Guid.TryParseExact(request.RequestId, "N", out _) ||
            !Profiles.Contains(request.Profile, StringComparer.Ordinal) ||
            string.IsNullOrWhiteSpace(request.UserSid) ||
            (request.Profile == "job-only-complete" ? request.ContainerSid is not null :
                string.IsNullOrWhiteSpace(request.ContainerSid)))
            throw new InvalidDataException("Invalid fixed-trial collection identity.");
        string scratch = Path.GetFullPath(request.Scratch);
        string leaf = Path.GetFileName(scratch);
        if (!scratch.Equals(request.Scratch, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetDirectoryName(scratch)!.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(evidenceRoot)),
                StringComparison.OrdinalIgnoreCase) ||
            !leaf.StartsWith("kora.r02.", StringComparison.Ordinal) ||
            !Guid.TryParseExact(leaf["kora.r02.".Length..], "N", out _))
            throw new InvalidDataException("Collection must target the exact owned proof tree within this evidence directory.");
        if (request.StartedUtc.Offset != TimeSpan.Zero || request.EndedUtc.Offset != TimeSpan.Zero ||
            request.EndedUtc < request.StartedUtc || request.EndedUtc > now ||
            now - request.StartedUtc >= RequestAgeLimit)
            throw new InvalidDataException("Invalid, future or expired collection window.");
        if (request.Port is < 1 or > 65535 || !IPAddress.TryParse(request.Address, out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast))
            throw new InvalidDataException("Collection requires a fixed owned non-loopback IPv4 endpoint and port.");
        if (request.Images is not { Length: 2 })
            throw new InvalidDataException("Exactly two fixed interpreter/worker images required.");
        string[] names = ["dotnet", "powershell"];
        string[] paths = [Path.Combine(scratch, "runtime", "proof", "ContainmentProof.exe"),
            Path.Combine(scratch, "runtime", "powershell", "pwsh.exe")];
        for (int i = 0; i < names.Length; i++)
        {
            if (request.Images[i].Name != names[i] ||
                !request.Images[i].Path.Equals(paths[i], StringComparison.OrdinalIgnoreCase) ||
                request.Images[i].Sha256 is not { Length: 64 } hash ||
                hash.Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidDataException("Unexpected image identity; collection cannot broaden its application scope.");
        }
    }

    internal static int TimeWindow(NetworkRequest request, DateTimeOffset now)
    {
        double seconds = (now - request.StartedUtc).TotalSeconds;
        if (seconds < 0 || seconds >= RequestAgeLimit.TotalSeconds)
            throw new InvalidDataException("Expired or future collection request.");
        return Math.Max(1, (int)Math.Ceiling(seconds));
    }

    internal static NetworkObserverMode ObserverMode(string command, string consent) =>
        (command, consent) switch
        {
            ("collect-network", "consent-filtered-buffered-events") => NetworkObserverMode.Strict,
            ("collect-network-user-comparison", "consent-application-endpoint-events-without-user-filter")
                => NetworkObserverMode.UserFilterComparison,
            _ => throw new ArgumentException("Exact mode-specific collection consent required; no queries made."),
        };

    internal static NetworkQueryScope[] QueryScopes(NetworkObserverMode mode) => mode switch
    {
        NetworkObserverMode.Strict => [NetworkQueryScope.Strict],
        NetworkObserverMode.UserFilterComparison =>
            [NetworkQueryScope.Strict, NetworkQueryScope.ApplicationOnlyDiagnostic],
        _ => throw new InvalidDataException("Unknown observer mode."),
    };

    internal static TimeSpan QueryTimeout(NetworkObserverMode mode, TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
            throw new InvalidDataException("Invalid query budget observation.");
        if (mode == NetworkObserverMode.Strict) return TimeSpan.FromSeconds(10);
        if (mode != NetworkObserverMode.UserFilterComparison)
            throw new InvalidDataException("Unknown observer mode.");
        TimeSpan remaining = ComparisonQueryBudget - elapsed;
        if (remaining <= TimeSpan.Zero)
            throw new TimeoutException("Comparison query budget exhausted; no additional query or replay.");
        return remaining < TimeSpan.FromSeconds(10) ? remaining : TimeSpan.FromSeconds(10);
    }

    internal static string[] QueryArguments(NetworkRequest request, NetworkImage image,
        string address, string destination, DateTimeOffset now, NetworkQueryScope scope = NetworkQueryScope.Strict)
    {
        if (!request.Images.Contains(image) ||
            (address != IPAddress.Loopback.ToString() && address != request.Address))
            throw new InvalidDataException("Query outside fixed application/endpoint scope.");
        List<string> arguments = ["wfp", "show", "netevents", $"file={destination}", "protocol=6",
            $"remoteaddr={address}", $"remoteport={request.Port}", $"appid={image.Path}"];
        if (scope == NetworkQueryScope.Strict) arguments.Add($"userid={request.UserSid}");
        else if (scope != NetworkQueryScope.ApplicationOnlyDiagnostic)
            throw new InvalidDataException("Unknown query scope; no arbitrary filter removal.");
        arguments.Add($"timewindow={TimeWindow(request, now)}");
        return arguments.ToArray();
    }

    internal static async Task AwaitCompletionAsync(string requestId, string digest,
        Func<Task<NetworkCompletion?>> read, Func<TimeSpan> elapsed, Func<Task> pause)
    {
        while (elapsed() < HostWaitLimit)
        {
            NetworkCompletion? completion = await read();
            if (completion is not null)
            {
                if (completion.RequestId != requestId || completion.RequestSha256 != digest ||
                    completion.Attribution != "Unproven")
                    throw new InvalidDataException("Uncorrelated or authority-claiming collector receipt.");
                if (completion.Status != "CollectedForInspection")
                    throw new InvalidDataException($"Collector failed; no replay: {completion.Error ?? completion.Status}");
                return;
            }
            await pause();
        }
        throw new TimeoutException("Collector did not complete within 90 seconds; cleanup continues, no replay.");
    }

    internal static void InspectXml(Stream input)
    {
        if (!input.CanSeek || input.Length > MaximumXmlBytes)
            throw new InvalidDataException("Filtered XML exceeds 1 MiB or has unverifiable size.");
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumXmlBytes,
        });
        while (reader.Read()) { }
    }
}
