using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed record InCallFeedbackCommandResult(string Outcome, string? Recovery, long Revision, long CallRevision,
    InCallFeedbackMode? Saved, InCallFeedbackMode? Desired, string Source, bool Available)
{
    public int Schema => 1;
    public string Id => InCallFeedbackCommand.OptionId;
    public string Type => "in-call-feedback-mode";
    public InCallFeedbackMode Default => InCallFeedbackRules.Default;
    public IReadOnlyList<InCallFeedbackMode> Choices => Enum.GetValues<InCallFeedbackMode>();
    public string Scope => "device-local";
    public string Effect => "in-call-response-selection-only; never speech permission or input authority";
    public string ApplicationTiming => "after-audited-atomic-save; retires old output; next eligible response only";
    public string ResetEffect => "Removes only this override, restoring unsaved UI default; no replay or capture.";
    public string Precedence => "Active/Suspected override (unless Inherit) > task > queue > session > device; all hard speech/privacy/visual gates remain";
    public string Confirmation => "host-held choice; original input and session/generation/configuration/call/name/native-lifetime revisions";
    public string Syntax => InCallFeedbackCommand.Syntax;
    public CallState CallState { get; init; }
    public bool Applied { get; init; }
    public ResponseOutputMode? Effective { get; init; }
    public bool SpeechEligible { get; init; }
    public bool MandatoryVisual { get; init; }
    public string? OutputPolicy { get; init; }

    public static string Serialize(InCallFeedbackCommandResult result)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result, Json);
        if (bytes.Length > SessionCommand.MaximumResultBytes)
        {
            throw new InvalidDataException("Call feedback result exceeds 64 KiB. No partial result was presented.");
        }
        return Encoding.UTF8.GetString(bytes);
    }

    private static readonly JsonSerializerOptions Json = CreateJson();
    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter<InCallFeedbackMode>());
        options.Converters.Add(new JsonStringEnumConverter<CallState>());
        options.Converters.Add(new JsonStringEnumConverter<ResponseOutputMode>());
        return options;
    }
}
