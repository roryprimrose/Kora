using Kora.Core.Dependencies;
using Kora.Core.Voice;
using System.Globalization;

namespace Kora.Application.Configuration;

public sealed class LocalSpeechTextPreferences : ISpeechTextPreferences
{
    private const string FileName = "speech-text-mode.txt";
    private const string PendingFileName = "speech-text-mode-unconfirmed.txt";
    private const string OptionsFileName = "speech-caption-options.txt";
    private readonly IPreferenceStore store;

    public LocalSpeechTextPreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalSpeechTextPreferences(IPreferenceStore store) => this.store = store;

    public SpeechTextMode? Load()
    {
        if (store.ReadText(PendingFileName) is not null)
        {
            throw new InvalidDataException("Speech-text evidence is unconfirmed. Inspect saved state and audit receipts before explicit repair.");
        }
        return ReadBack();
    }

    public SpeechTextMode? ReadBack() => store.ReadText(FileName) switch
    {
        null => null,
        "Off" => SpeechTextMode.Off,
        "CurrentUtterance" => SpeechTextMode.CurrentUtterance,
        _ => throw new InvalidDataException("The saved speech-text mode is invalid."),
    };

    public void BeginWrite() => store.WriteText(PendingFileName, "1");
    public void ConfirmWrite() => store.Delete(PendingFileName);
    public void Save(SpeechTextMode mode)
    {
        if (!Enum.IsDefined(mode)) { throw new ArgumentOutOfRangeException(nameof(mode)); }
        store.WriteText(FileName, mode.ToString());
    }

    public SpeechCaptionOptions? LoadOptions()
    {
        _ = Load();
        return ReadBackOptions();
    }

    public SpeechCaptionOptions? ReadBackOptions()
    {
        var content = store.ReadLines(OptionsFileName);
        if (content is null) { return null; }
        if (content.Length != 3 || !string.Equals(content[0], "1", StringComparison.Ordinal)
            || !Enum.TryParse<SpeechCaptionPlacement>(content[1], out var placement)
            || !string.Equals(placement.ToString(), content[1], StringComparison.Ordinal)
            || !int.TryParse(content[2], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || !string.Equals(seconds.ToString(CultureInfo.InvariantCulture), content[2], StringComparison.Ordinal))
        {
            throw new InvalidDataException("The saved caption options are invalid.");
        }
        try { return new(placement, seconds); }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("The saved caption options are invalid.", exception);
        }
    }

    public void SaveOptions(SpeechCaptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        store.WriteLines(OptionsFileName, ["1", options.Placement.ToString(),
            options.DismissalDelaySeconds.ToString(CultureInfo.InvariantCulture)]);
    }
}
