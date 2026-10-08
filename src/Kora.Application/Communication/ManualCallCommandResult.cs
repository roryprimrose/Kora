using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kora.Core.Commands;
using Kora.Core.Communication;

namespace Kora.Application.Communication;

public sealed record ManualCallCommandResult(string Outcome, string? Recovery, CallPolicyObservation Observation)
{
    public int Schema => 1;
    public string Id => ManualCallCommand.OptionId;
    public string Type => "run-only-boolean";
    public bool Default => false;
    public string Scope => "current-process-run; never persisted or restored";
    public string Source => "host-cached-manual-layer-and-independent-automatic-observation";
    public long SourceRevision => Observation.Revision;
    public bool AutomaticDetectorAvailable => Observation.AutomaticState != CallState.Unavailable
        && Enum.IsDefined(Observation.AutomaticState);
    public string AutomaticEvidence => "An observation, not source certainty; Unavailable never means Clear and Unknown stays conservative.";
    public string EvidencePolicy => Observation.ManualControlEvidenceUnavailable
        ? "Required manual control evidence or resource closure is unavailable; conservative speech/activation/reusable-grant protection remains held."
        : "Manual control evidence is not currently held unavailable; automatic observations and normal host gates still apply.";
    public string ResetEffect => "Manual off only; preserves automatic observations, saved call flags, grants and mandatory visual content.";
    public string ApplicationTiming => "Required requested audit precedes fenced process-memory transition; durable outcome is not atomic persistence of the manual flag.";
    public string Confirmation => "Original initiating channel, own live host context, committed intent, session generation, ownership/privacy/input and current source/policy revision.";
    public string Syntax => ManualCallCommand.Syntax;

    public static string Serialize(ManualCallCommandResult result)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result, Json);
        if (bytes.Length > SessionCommand.MaximumResultBytes)
        {
            throw new InvalidDataException("Call result exceeds 64 KiB. No partial result was presented.");
        }
        return Encoding.UTF8.GetString(bytes);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<CallState>() },
    };
}
