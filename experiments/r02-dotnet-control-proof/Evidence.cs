using System.Runtime.InteropServices;
using System.Text.Json;

namespace Kora.Rt1;

internal sealed record ProofRow(string Id, string Status, DateTimeOffset ObservedAtUtc,
    Dictionary<string, long> Counters, Dictionary<string, string> Facts);

internal static class Evidence
{
    private static readonly Dictionary<string, ProofRow> rows = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    internal static async Task RecordAsync(ProofRow row)
    {
        rows[row.Id] = row;
        var directory = Path.Combine(Candidate.Root, "evidence");
        Directory.CreateDirectory(directory);
        var report = new
        {
            RecordedAtUtc = DateTimeOffset.UtcNow,
            Profile = "exact-tag-source-build, Windows x64, child stdio, empty mode, synthetic HTTP/SSE only",
            SdkSourceCommit = Candidate.SourceCommit, Sdk = Candidate.SdkVersion,
            SdkAssemblySha256 = await Candidate.HashAsync(typeof(GitHub.Copilot.CopilotClient).Assembly.Location),
            SdkPackageSha256 = await Candidate.HashAsync(Path.Combine(Candidate.Root, ".candidate", "feed",
                "GitHub.Copilot.SDK." + Candidate.SdkVersion + ".nupkg")),
            FixtureAssemblySha256 = await Candidate.HashAsync(typeof(Evidence).Assembly.Location),
            FixtureSources = await Task.WhenAll(Directory.EnumerateFiles(Candidate.Root, "*")
                .Where(path => Path.GetExtension(path) is ".cs" or ".ps1" or ".csproj" or ".props")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(async path => new { File = Path.GetFileName(path), Sha256 = await Candidate.HashAsync(path) })),
            Runtime = "1.0.90", Protocol = 3,
            NativeLauncherSha256 = await Candidate.HashAsync(Candidate.Executable),
            NativePayloadSha256 = await Candidate.HashAsync(Path.Combine(Candidate.RuntimeDirectory, "runtime.node")),
            DotnetRuntime = RuntimeInformation.FrameworkDescription, Os = Environment.OSVersion.VersionString,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            SyntheticOnly = true, LiveInferenceCalls = 0, ModelChargesUsd = 0, ProductionEnabled = false,
            Scope = "RT1 public controls only; not RT2, MG1, PV1, production scheduler or installed acceptance",
            Rows = rows.Values.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray()
        };
        await File.WriteAllTextAsync(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(report, options) + Environment.NewLine);
    }
}

internal sealed class Trial(RuntimeFixture runtime, SyntheticProvider provider, SyntheticProvider manager)
{
    internal RuntimeFixture Runtime { get; } = runtime;
    internal SyntheticProvider Provider { get; } = provider;
    internal SyntheticProvider Manager { get; } = manager;
    internal Dictionary<string, long> Counters { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, string> Facts { get; } = new(StringComparer.Ordinal);
    internal string CapabilityStatus { get; set; } = "PASS";
}
