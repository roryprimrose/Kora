using Kora.Core.Dependencies;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed class LocalVoiceCaptureTimingPreferences : IVoiceCaptureTimingPreferences
{
    private const string FileName = "voice-capture-timing.txt";
    private const string PendingFileName = "voice-capture-timing-unconfirmed.txt";
    private readonly IPreferenceStore store;
    public LocalVoiceCaptureTimingPreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalVoiceCaptureTimingPreferences(IPreferenceStore store) => this.store = store;
    public VoiceCaptureTiming? Load()
    {
        if (store.ReadText(PendingFileName) is not null)
        { throw new InvalidDataException("Capture timing has an unconfirmed write. Inspect saved state and audit before repair; capture remains unavailable."); }
        return ReadBack();
    }
    public VoiceCaptureTiming? ReadBack() => store.ReadText(FileName, 256) is { } text ? VoiceCaptureTiming.Parse(text) : null;
    public void BeginWrite() => store.WriteText(PendingFileName, "1");
    public void Save(VoiceCaptureTiming timing) => store.WriteText(FileName, timing.Serialize());
    public void ConfirmWrite() => store.Delete(PendingFileName);
}
