using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kora.Core.Commands;
using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

public sealed record ProviderModeCommandResult(string Outcome, string? Recovery, long Revision, long CallRevision,
    ModelProviderMode? Saved, ModelProviderMode? Desired, string Source, bool Available)
{
    public int Schema => 1;
    public string Id => ProviderModeCommand.OptionId;
    public string Type => "model-provider-mode";
    public ModelProviderMode Default => ModelProviderModePreference.Default;
    public IReadOnlyList<ModelProviderMode> Choices => ModelProviderModePreference.Choices;
    public string Scope => "device-local";
    public string Effect => "initial-session-provider-policy-only; no qualification, account, network or egress authority";
    public string ApplicationTiming => "after-audited-atomic-save; first policy-bound turn of subsequent sessions only";
    public string ResetEffect => "Saves LocalOnly only; existing session policies, turn choices, permissions and consent are unchanged.";
    public string Precedence => "explicit per-turn Default/Local/Hosted uses the existing session policy; LocalOnly refuses Hosted";
    public string Confirmation => "host-held choice, admitted session/generation, original channel/live host/call and preference revisions";
    public string Syntax => ProviderModeCommand.Syntax;

    public static string Serialize(ProviderModeCommandResult result)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result, Json);
        if (bytes.Length > SessionCommand.MaximumResultBytes)
        {
            throw new InvalidDataException("Provider-mode result exceeds 64 KiB. No partial result was presented.");
        }
        return Encoding.UTF8.GetString(bytes);
    }

    private static readonly JsonSerializerOptions Json = CreateJson();
    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter<ModelProviderMode>());
        return options;
    }
}
