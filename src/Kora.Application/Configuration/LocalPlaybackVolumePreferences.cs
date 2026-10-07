using System.Globalization;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed class LocalPlaybackVolumePreferences : IPlaybackVolumePreferences
{
    private const string FileName = "speech-playback-volume.txt";
    private const string PendingFileName = "speech-playback-volume-unconfirmed.txt";
    private readonly IPreferenceStore store;

    public LocalPlaybackVolumePreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalPlaybackVolumePreferences(IPreferenceStore store) => this.store = store;

    public PlaybackVolume? Load()
    {
        if (store.ReadText(PendingFileName) is not null)
        {
            throw new InvalidDataException("A playback-volume write has unconfirmed evidence. Inspect saved volume and audit receipts before explicit repair and refresh.");
        }
        return ReadBack();
    }

    public void BeginWrite() => store.WriteText(PendingFileName, "1");
    public void ConfirmWrite() => store.Delete(PendingFileName);

    public PlaybackVolume? ReadBack()
    {
        var lines = store.ReadLines(FileName);
        if (lines is null) { return null; }
        if (lines.Length != 2 || !string.Equals(lines[0], "1", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The saved playback volume format is unknown or malformed.");
        }
        try { return PlaybackVolume.Parse(lines[1]); }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("The saved playback volume is invalid.", exception);
        }
    }

    public void Save(PlaybackVolume volume) =>
        store.WriteLines(FileName, ["1", volume.Percent.ToString(CultureInfo.InvariantCulture)]);

    public void Reset() => store.Delete(FileName);
}
