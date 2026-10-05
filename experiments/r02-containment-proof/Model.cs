using System.Text.Json;

namespace ContainmentProof;

internal sealed record Probe(string Name, string Outcome, int? NativeError = null, string? Detail = null);
internal sealed record Spec(string RunId, string Allowed, string Protected, string Alias,
    string Credential, int Port, string Address, string PowerShell, string Executable, bool LoseReceipt);
internal sealed record Receipt(string RunId, int Pid, TokenFacts Token, List<Probe> Probes,
    List<int> Children);
internal sealed record TokenFacts(bool AppContainer, string UserSid, string? ContainerSid,
    bool Elevated = false, string? IntegritySid = null);
internal sealed record Trial(string Profile, string State, string EffectOutcome, int? ExitCode,
    int? LaunchError, Receipt? Receipt, List<int> JobPids, bool TreeStopped,
    double ShutdownMilliseconds, bool ProtectedBytesUnchanged, List<TokenFacts> ChildTokens,
    bool EffectMarkerExists, string? Failure);

internal static class Wire
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal static void Write<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    internal static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException($"Missing {typeof(T).Name} in {path}");

    // Exit and cancellation are lifecycle observations, not proof of an effect.
    internal static string EffectOutcome(string runId, Receipt? receipt) =>
        receipt is null || receipt.RunId != runId ? "Unknown" : "Observed";
}
