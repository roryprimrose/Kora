using System.Collections.Immutable;
using System.Text.Json;
using Kora.Core.Commands;

namespace Kora.Application.Configuration;

public sealed record OutputDeviceCommandResult(string Outcome, string? Recovery, long Revision,
    long CallRevision, bool MetadataCurrent, string? Desired, string? Effective, string? SystemDefault,
    string Source, bool Available, bool Muted)
{
    public int Schema => 1;
    public string Id => OutputDeviceCommand.OptionId;
    public string Type => "exact-endpoint-choice";
    public string Default => "system-default";
    public string Scope => "device-local";
    public string Effect => "output-preference-only";
    public string ApplicationTiming => "after-audited-atomic-save; invalidates old output; next eligible speech only";
    public string ResetEffect => "Selects System; preserves provider, voice, summary, input and consent.";
    public string Confirmation => "host-held exact choice, admitted session/generation, live host/original channel/call and revisions";
    public string Syntax => OutputDeviceCommand.Syntax;
    public ImmutableArray<OutputDeviceCommandChoice> Choices { get; init; } = [];

    public static string Serialize(OutputDeviceCommandResult result)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result, Json);
        if (bytes.Length > SessionCommand.MaximumResultBytes)
        {
            throw new InvalidDataException("Output-device result exceeds 64 KiB. No partial choices were presented; reduce the local catalogue and refresh.");
        }
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
