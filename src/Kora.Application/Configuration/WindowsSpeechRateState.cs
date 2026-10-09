using System.Text.Json;
using Kora.Core.Commands;
using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed record WindowsSpeechRateState(WindowsSpeechRate? Desired, WindowsSpeechRate? Effective,
    string? ProviderId, SpeechRateSupport Support, long ProviderRevision, string Source, long Revision, string? Recovery)
{
    public int Schema => 1;
    public string Id => WindowsSpeechRateCommand.OptionId;
    public string Type => "integer-windows-native-rate";
    public int Minimum => WindowsSpeechRate.Minimum;
    public int Maximum => WindowsSpeechRate.Maximum;
    public int Default => WindowsSpeechRate.Default.Value;
    public string Scope => "device-local; Kora-owned Windows synthesizer only; Kokoro unchanged";
    public bool Available => Effective is not null;
    public bool AllowsProviderOutput(string providerId) =>
        string.Equals(providerId, SpeechProviderIds.Kokoro, StringComparison.Ordinal)
        || string.Equals(providerId, SpeechProviderIds.Windows, StringComparison.Ordinal) && Available;
    public string ApplicationTiming => "audited atomic save; retires active and queued output; future eligible Windows synthesis only";
    public string Confirmation => "host-held proposal; original input; active audio session/generation; live host/privacy/call, provider/source and revisions";
    public string ResetEffect => "removes only Windows rate override; restores engine-normal 0; no replay";
    public string Syntax => WindowsSpeechRateCommand.Syntax;

    public static string Serialize(WindowsSpeechRateState state, long callRevision, string outcome)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { State = state, CallRevision = callRevision, Outcome = outcome }, Json);
        if (bytes.Length > SessionCommand.MaximumResultBytes)
        {
            throw new InvalidDataException("Rate result exceeds 64 KiB; no partial result is presented.");
        }
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
