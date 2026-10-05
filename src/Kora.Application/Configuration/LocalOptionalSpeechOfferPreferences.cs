using System.Text.Json;

using Kora.Core.Dependencies;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed class LocalOptionalSpeechOfferPreferences : IOptionalSpeechOfferPreferences
{
    private const string FileName = "optional-speech-offer.json";
    private readonly IPreferenceStore store;

    public LocalOptionalSpeechOfferPreferences(IApplicationDataPaths paths)
        : this(new LocalPreferenceStore(paths))
    {
    }

    internal LocalOptionalSpeechOfferPreferences(IPreferenceStore store)
    {
        this.store = store;
    }

    public OptionalSpeechOfferState Load()
    {
        var contents = store.ReadText(FileName);
        if (contents is null)
        {
            return new OptionalSpeechOfferState(false, null);
        }

        using var document = JsonDocument.Parse(contents);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(nameof(OptionalSpeechOfferState.InitialOfferHandled), out var handled)
            || handled.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !root.TryGetProperty(nameof(OptionalSpeechOfferState.MissingProviderNotified), out var missing)
            || missing.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))
        {
            throw new InvalidDataException("The saved optional speech offer state has an invalid format.");
        }

        return new OptionalSpeechOfferState(
            handled.GetBoolean(),
            missing.ValueKind == JsonValueKind.Null ? null : missing.GetString());
    }

    public void Save(OptionalSpeechOfferState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        store.WriteText(FileName, JsonSerializer.Serialize(state));
    }
}
