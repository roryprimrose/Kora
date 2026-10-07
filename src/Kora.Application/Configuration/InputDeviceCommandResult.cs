using System.Collections.Immutable;
using System.Text.Json;

using Kora.Core.Commands;

namespace Kora.Application.Configuration;

public sealed record InputDeviceCommandResult(
    string Outcome, string? Recovery, long Revision, long CallRevision, bool MetadataCurrent,
    string? Desired, string? Effective, string Source, string Readiness)
{
    public int Schema => 1;
    public string Id => InputDeviceCommand.OptionId;
    public string Type => "exact-endpoint-choice";
    public string Default => "system-default";
    public string Scope => "device-local";
    public string Effect => "input-preference-only";
    public string ApplicationTiming => "after-atomic-save; next separately enabled capture";
    public string ResetEffect => "Selects System; retains consent, permission and run holds.";
    public string Confirmation => "host-held metadata choice; current revision, original-channel and call gates";
    public string Syntax => InputDeviceCommand.Syntax;
    public ImmutableArray<InputDeviceCommandChoice> Choices { get; init; } = [];

    public static string Serialize(InputDeviceCommandResult result)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result, Json);
        if (bytes.Length > SessionCommand.MaximumResultBytes)
        {
            throw new InvalidDataException("Input-device result exceeds 64 KiB. No partial choices were presented; reduce the local catalogue and refresh.");
        }
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
