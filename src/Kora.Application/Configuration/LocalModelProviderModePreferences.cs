using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

public sealed class LocalModelProviderModePreferences : IModelProviderModePreferences
{
    private const string FileName = "provider-mode.txt";
    private const string PendingFileName = "provider-mode-unconfirmed.txt";
    private readonly IPreferenceStore store;

    public LocalModelProviderModePreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalModelProviderModePreferences(IPreferenceStore store) => this.store = store;

    public ModelProviderMode? Load()
    {
        if (store.ReadText(PendingFileName, 64) is not null)
        {
            throw new InvalidDataException("A provider-mode write has unconfirmed evidence. Inspect saved state and audit/control receipts before explicit repair and refresh.");
        }
        return ReadBack();
    }

    public ModelProviderMode? ReadBack()
    {
        var text = store.ReadText(FileName, 64);
        if (text is null) { return null; }
        using var reader = new StringReader(text);
        if (!string.Equals(reader.ReadLine(), "1", StringComparison.Ordinal)
            || reader.ReadLine() is not { } value || reader.ReadLine() is not null)
        {
            throw new InvalidDataException("The saved provider-mode format is unknown or malformed.");
        }
        return ModelProviderModePreference.Parse(value);
    }

    public void BeginWrite() => store.WriteText(PendingFileName, "1");
    public void Save(ModelProviderMode mode) => store.WriteLines(FileName, ["1", ModelProviderModePreference.Serialize(mode)]);
    public void ConfirmWrite() => store.Delete(PendingFileName);
}
