using System.Text.Json;
using Kora.Core.Commands;
using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record PlaybackVolumeState(PlaybackVolume? Desired, PlaybackVolume? Effective,
    string Source, long Revision, string? Recovery)
{
    public int Schema => 1;
    public string Id => PlaybackVolumeCommand.OptionId;
    public string Type => "integer-percent";
    public int Minimum => 0;
    public int Maximum => PlaybackVolume.MaximumPercent;
    public int Default => PlaybackVolume.Default.Percent;
    public string Scope => "device-local; Kora-owned speech only";
    public bool Available => Effective is not null;
    public bool AllowsSpeech => Effective?.AllowsSpeech == true;
    public string ApplicationTiming => "audited atomic save; retires active and queued output; next eligible speech only";
    public string Confirmation => "host-held proposal; original input; active audio session/generation; live host/privacy/call and revisions";
    public string ResetEffect => "removes only volume override; restores unscaled 100; no replay";
    public string Syntax => PlaybackVolumeCommand.Syntax;

    public static string Serialize(PlaybackVolumeState state, long callRevision, string outcome)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { State = state, CallRevision = callRevision, Outcome = outcome }, Json);
        if (bytes.Length > SessionCommand.MaximumResultBytes)
        {
            throw new InvalidDataException("Volume result exceeds 64 KiB; no partial result is presented.");
        }
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
