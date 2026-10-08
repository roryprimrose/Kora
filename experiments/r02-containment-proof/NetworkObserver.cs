using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Xml;

namespace ContainmentProof;

internal static class NetworkObserver
{
    internal static async Task<int> RunAsync(string output, NetworkObserverMode mode)
    {
        NetworkQueryScope[] scopes = NetworkCollection.QueryScopes(mode);
        string directory = Path.GetFullPath(output);
        var token = Native.Token();
        if (!token.Elevated || token.AppContainer)
            throw new InvalidOperationException("Separate administrator observer required; it never launches a worker.");
        RejectReparseAncestors(directory);
        Console.WriteLine("Metadata-filtered buffered-event observer only; no tracing or policy changes. Attribution unproven.");
        if (mode == NetworkObserverMode.UserFilterComparison)
            Console.WriteLine("Approved comparison omits only userid in diagnostic queries; other users' matching metadata may be returned.");
        foreach (string profile in NetworkCollection.Profiles)
        {
            string requestPath = Path.Combine(directory, $"network-{profile}.request.json");
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(requestPath) && deadline.Elapsed < TimeSpan.FromSeconds(90))
                await Task.Delay(200);
            if (!File.Exists(requestPath))
                throw new TimeoutException($"No fixed request for {profile} within 90 seconds; no queries made.");
            RejectReparseAncestors(requestPath);
            if (new FileInfo(requestPath).Length > NetworkCollection.MaximumJsonBytes)
                throw new InvalidDataException("Collection request exceeds 64 KiB.");
            byte[] bytes = await File.ReadAllBytesAsync(requestPath);
            string digest = Convert.ToHexString(SHA256.HashData(bytes));
            var request = JsonSerializer.Deserialize<NetworkRequest>(bytes, Wire.Json)
                ?? throw new InvalidDataException("Empty network request.");
            NetworkCollection.Validate(request, Path.GetTempPath(), DateTimeOffset.UtcNow);
            if (request.Profile != profile || request.UserSid != token.UserSid)
                throw new InvalidDataException("Observer must be the same user's separate elevated token and exact profile.");
            string completionPath = Path.Combine(directory, $"network-{profile}.completed.json");
            if (File.Exists(completionPath))
                throw new InvalidDataException("Collector completion already exists; never overwrite or replay.");
            string evidence = Path.Combine(directory, $"network-{profile}-{request.RequestId}");
            if (Directory.Exists(evidence))
                throw new InvalidDataException("Collector output must be new.");
            Directory.CreateDirectory(evidence);
            var queries = new List<NetworkQuery>();
            string? failure = null;
            bool completed = false;
            var queryBudget = Stopwatch.StartNew();
            try
            {
                bool ownedAddress = NetworkInterface.GetAllNetworkInterfaces()
                    .SelectMany(item => item.GetIPProperties().UnicastAddresses)
                    .Any(item => item.Address.ToString() == request.Address);
                if (!ownedAddress) throw new InvalidDataException("Endpoint is not a current local interface.");
                foreach (NetworkImage image in request.Images)
                {
                    RejectReparseAncestors(image.Path);
                    if (Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(image.Path))) != image.Sha256)
                        throw new InvalidDataException("Copied trial image changed before collection.");
                    foreach (string address in new[] { IPAddress.Loopback.ToString(), request.Address })
                    {
                        foreach (NetworkQueryScope scope in scopes)
                        {
                            DateTimeOffset started = DateTimeOffset.UtcNow;
                            NetworkCollection.Validate(request, Path.GetTempPath(), started);
                            string suffix = mode == NetworkObserverMode.Strict ? "" :
                                scope == NetworkQueryScope.Strict ? "-strict" : "-application-only-diagnostic";
                            string xml = Path.Combine(evidence,
                                $"{image.Name}-{(address == request.Address ? "interface" : "loopback")}{suffix}.xml");
                            string[] arguments = NetworkCollection.QueryArguments(request, image, address, xml, started, scope);
                            TimeSpan limit = NetworkCollection.QueryTimeout(mode, queryBudget.Elapsed);
                            var result = await QueryAsync(arguments, limit);
                            queries.Add(new(image.Name, address, scope.ToString(), started, DateTimeOffset.UtcNow,
                                arguments, result.ExitCode, result.Output));
                            if (result.ExitCode != 0 || !File.Exists(xml))
                                throw new InvalidDataException("Filtered netsh query failed or produced no XML; not denial evidence.");
                            await using var input = File.OpenRead(xml);
                            NetworkCollection.InspectXml(input);
                        }
                    }
                }
                completed = true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                InvalidDataException or XmlException or System.ComponentModel.Win32Exception or TimeoutException)
            {
                failure = error.Message;
                throw;
            }
            finally
            {
                Wire.Write(Path.Combine(evidence, "collection.json"), new
                {
                    Request = request, RequestSha256 = digest, Mode = mode.ToString(), Queries = queries,
                    Error = failure ?? (completed ? null : "Collector did not complete."), Utc = DateTimeOffset.UtcNow,
                    XmlSchemaAndEventAttributionQualified = false,
                    ProductionProfileCertified = false,
                });
                var completion = new NetworkCompletion(request.RequestId, digest,
                    completed ? "CollectedForInspection" : "Failed", "Unproven",
                    failure ?? (completed ? null : "Collector did not complete."));
                string temporary = completionPath + ".tmp";
                Wire.Write(temporary, completion);
                File.Move(temporary, completionPath);
            }
        }
        Console.WriteLine("Filtered XML retained; no automatic denial classification or W1 acceptance.");
        return 2;
    }

    private static async Task<(int ExitCode, string Output)> QueryAsync(string[] arguments, TimeSpan limit)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start the fixed read-only netsh query.");
        using var timeout = new CancellationTokenSource(limit);
        Task<string> stdout = ReadOutputAsync(process.StandardOutput, timeout.Token);
        Task<string> stderr = ReadOutputAsync(process.StandardError, timeout.Token);
        try { await Task.WhenAll(process.WaitForExitAsync(timeout.Token), stdout, stderr); }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException("Owned read-only netsh query exceeded its bounded deadline; no automatic retry.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        return (process.ExitCode, (await stdout) + (await stderr));
    }

    private static async Task<string> ReadOutputAsync(StreamReader reader, CancellationToken cancellation)
    {
        var output = new StringBuilder();
        char[] buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer, cancellation)) != 0)
        {
            if (output.Length + read > NetworkCollection.MaximumJsonBytes)
                throw new InvalidDataException("Native query diagnostic output exceeds 64 Ki characters.");
            output.Append(buffer, 0, read);
        }
        return output.ToString();
    }

    private static void RejectReparseAncestors(string path)
    {
        for (string? candidate = path; candidate is not null; candidate = Path.GetDirectoryName(candidate))
        {
            if ((File.Exists(candidate) || Directory.Exists(candidate)) &&
                (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Reparse paths are not admitted for this experimental observer.");
        }
    }

    private sealed record NetworkQuery(string Image, string Address, string Scope, DateTimeOffset StartedUtc,
        DateTimeOffset EndedUtc, string[] Arguments, int ExitCode, string Output);
}
