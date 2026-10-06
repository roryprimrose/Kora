using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Kora.Rt1;

namespace Kora.Mg1;

internal sealed record ProofRow(string Id, string Status, DateTimeOffset ObservedAtUtc,
    Dictionary<string, long> Counts, Dictionary<string, string> Facts);

internal static class ProofEvidence
{
    internal static string Root { get; } = SourceDirectory();
    internal static ConcurrentQueue<string> BackgroundFailures { get; } = new();
    private static readonly Dictionary<string, ProofRow> rows = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static string SourceDirectory([CallerFilePath] string file = "") => Path.GetDirectoryName(file)!;
    internal static async Task RecordAsync(ProofRow row)
    {
        rows.Add(row.Id, row);
        var report = new
        {
            RecordedAtUtc = DateTimeOffset.UtcNow,
            Profile = "Separately approved released NuGet 1.0.16, Windows x64 .NET 10.0.12 minimal HTTP/stdio; original RT1 native bytes",
            Sdk = Candidate.SdkVersion, SourceCommit = Candidate.SourceCommit,
            SdkAssemblySha256 = await Candidate.HashAsync(typeof(GitHub.Copilot.CopilotClient).Assembly.Location),
            RuntimeManagedDependencies = await Task.WhenAll(new[]
            {
                typeof(Microsoft.Extensions.AI.AIFunction).Assembly,
                typeof(Microsoft.Extensions.Logging.ILogger).Assembly,
                typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly
            }.Select(async assembly => new
            {
                Id = assembly.GetName().Name,
                Version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? throw new InvalidDataException("managed-dependency-version-missing"),
                Sha256 = await Candidate.HashAsync(assembly.Location)
            })),
            Runtime = "1.0.90", Protocol = 3,
            NativeLauncherSha256 = await Candidate.HashAsync(Candidate.Executable),
            NativePayloadSha256 = await Candidate.HashAsync(Path.Combine(Candidate.RuntimeDirectory, "runtime.node")),
            FixtureAssemblySha256 = await Candidate.HashAsync(typeof(ProofEvidence).Assembly.Location),
            SdkPackageSha256 = await Candidate.HashAsync(Path.Combine(Root, ".inputs", "GitHub.Copilot.SDK.1.0.16.nupkg")),
            DotnetRuntime = RuntimeInformation.FrameworkDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Os = Environment.OSVersion.VersionString,
            FixtureSources = await Task.WhenAll(Directory.EnumerateFiles(Root, "*")
                .Where(path => Path.GetExtension(path) is ".cs" or ".ps1" or ".csproj" or ".props" || Path.GetFileName(path).EndsWith(".lock.json", StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(async path => new { File = Path.GetFileName(path), Sha256 = await Candidate.HashAsync(path) })),
            Thresholds = new { SelectedContextBytes = 32768, CompleteRequestBytes = 32768, CompleteOutputBytes = 4096,
                DispatchDeadlineMs = 15000, DeadlineToleranceMs = 1000, InFlight = 1, AttemptsPerRollingHourPerProfile = 30 },
            Rows = rows.Values.OrderBy(value => value.Id, StringComparer.Ordinal),
            PhysicalComputationTermination = "Unknown; socket closure is not physical computation evidence",
            ProductionProtocol = "Not established", SourceBuildByteEquivalence = "Not claimed",
            Rt2 = "Independent lifecycle gate", Pv1 = "Blocked: no account/usage approval", ProductionEnabled = false
        };
        var directory = Path.Combine(Root, "evidence");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "runtime-results.json"), JsonSerializer.Serialize(report, options));
    }
}
