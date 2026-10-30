using System.Globalization;

using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

public sealed class LocalReaderTextScalePreferences : IReaderTextScalePreferences
{
    private const string FileName = "reader-text-scale-percent.txt";
    private const string PendingFileName = "reader-text-scale-unconfirmed.txt";
    private readonly IPreferenceStore store;

    public LocalReaderTextScalePreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalReaderTextScalePreferences(IPreferenceStore store) => this.store = store;

    public int? LoadReaderTextScalePercent()
    {
        if (store.ReadText(PendingFileName) is not null)
        {
            throw new InvalidDataException("Native reader scale is unconfirmed. Inspect preference evidence and audit receipts before explicit repair.");
        }
        return ReadBackReaderTextScalePercent();
    }

    public int? ReadBackReaderTextScalePercent()
    {
        var text = store.ReadText(FileName);
        if (text is null) { return null; }
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var percent)
            || !string.Equals(text, percent.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            throw new InvalidDataException("The saved native reader text scale is not a canonical integer percentage.");
        }
        try { ReaderTextScaleSettings.ValidatePercent(percent); }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("The saved native reader text scale is outside the admitted range.", exception);
        }
        return percent;
    }

    public void BeginReaderTextScaleWrite() => store.WriteText(PendingFileName, "1");
    public void SaveReaderTextScalePercent(int percent)
    {
        ReaderTextScaleSettings.ValidatePercent(percent);
        store.WriteText(FileName, percent.ToString(CultureInfo.InvariantCulture));
    }
    public void ConfirmReaderTextScaleWrite() => store.Delete(PendingFileName);
    internal void Reset() => store.Delete(FileName);
}
