using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kora.Core.Commands;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed record ResponseModeCommandResult(string Outcome, string? Recovery, long Revision, long CallRevision,
    ResponseOutputMode? Saved, ResponseOutputMode? Desired, ResponseOutputMode? Effective, string Source, bool Available)
{
    public int Schema => 1;
    public string Id => ResponseModeCommand.OptionId;
    public string Type => "response-output-mode";
    public ResponseOutputMode Default => ResponseOutputMode.Hybrid;
    public IReadOnlyList<ResponseOutputMode> Choices => Enum.GetValues<ResponseOutputMode>();
    public string Scope => "device-local";
    public string Effect => "device-default-response-output-only";
    public string ApplicationTiming => "after-audited-atomic-save; invalidates old output; next eligible response only";
    public string ResetEffect => "Saves Hybrid only; preserves task/queue/call, mute fallback, speech, input and consent.";
    public string Precedence => "Active/Suspected in-call feedback (unless Inherit) > task > queue > session > device; call/privacy/output suppression and required full visual always apply";
    public string Confirmation => "host-held choice, admitted session/generation, original channel/live host/call and preference revisions";
    public string Syntax => ResponseModeCommand.Syntax;
    public bool? SpeechEligible { get; init; }
    public bool? MandatoryVisual { get; init; }
    public string? OutputPolicy { get; init; }

    public static string Serialize(ResponseModeCommandResult result)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result, Json);
        if (bytes.Length > SessionCommand.MaximumResultBytes)
        {
            throw new InvalidDataException("Response-mode result exceeds 64 KiB. No partial result was presented.");
        }
        return Encoding.UTF8.GetString(bytes);
    }

    private static readonly JsonSerializerOptions Json = CreateJson();
    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter<ResponseOutputMode>());
        return options;
    }
}
