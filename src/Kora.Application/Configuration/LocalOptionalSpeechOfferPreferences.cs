using System.Text.Json;

using Kora.Core.Dependencies;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed class LocalOptionalSpeechOfferPreferences(IApplicationDataPaths paths)
    : IOptionalSpeechOfferPreferences
{
    private readonly string directory = Path.Combine(paths.LocalRoot, "Preferences");
    private readonly string path = Path.Combine(paths.LocalRoot, "Preferences", "optional-speech-offer.json");

    public OptionalSpeechOfferState Load()
    {
        if (!File.Exists(path))
        {
            return new OptionalSpeechOfferState(false, null);
        }

        var contents = File.ReadAllText(path);
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
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, "optional-speech-offer.tmp");
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state));
        File.Move(temporaryPath, path, overwrite: true);
    }
}
