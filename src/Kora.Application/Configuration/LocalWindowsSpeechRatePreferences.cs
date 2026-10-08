using System.Globalization;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed class LocalWindowsSpeechRatePreferences : IWindowsSpeechRatePreferences
{
    private const string FileName = "speech-windows-rate.txt";
    private const string PendingFileName = "speech-windows-rate-unconfirmed.txt";
    private readonly IPreferenceStore store;

    public LocalWindowsSpeechRatePreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalWindowsSpeechRatePreferences(IPreferenceStore store) => this.store = store;

    public WindowsSpeechRate? Load()
    {
        if (store.ReadText(PendingFileName) is not null)
        {
            throw new InvalidDataException("A Windows speech-rate write has unconfirmed evidence. Inspect saved rate and audit receipts before explicit repair and refresh.");
        }
        return ReadBack();
    }

    public void BeginWrite() => store.WriteText(PendingFileName, "1");
    public void ConfirmWrite() => store.Delete(PendingFileName);

    public WindowsSpeechRate? ReadBack()
    {
        var lines = store.ReadLines(FileName);
        if (lines is null) { return null; }
        if (lines.Length != 2 || !string.Equals(lines[0], "1", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The saved Windows speech-rate format is unknown or malformed.");
        }
        try { return WindowsSpeechRate.Parse(lines[1]); }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("The saved Windows speech rate is invalid.", exception);
        }
    }

    public void Save(WindowsSpeechRate rate) =>
        store.WriteLines(FileName, ["1", rate.Value.ToString(CultureInfo.InvariantCulture)]);

    public void Reset() => store.Delete(FileName);
}
