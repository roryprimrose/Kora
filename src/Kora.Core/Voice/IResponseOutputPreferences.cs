namespace Kora.Core.Voice;

public interface IResponseOutputPreferences
{
    ResponseOutputMode? LoadDefaultMode();

    void SaveDefaultMode(ResponseOutputMode mode);

    /// <summary>Removes the saved default-mode override.</summary>
    void ResetDefaultMode();

    ResponseOutputMode? ReadBackDefaultMode();

    void BeginDefaultModeWrite();

    void ConfirmDefaultModeWrite();

    bool? LoadMutedOutputVisualFallback();

    void SaveMutedOutputVisualFallback(bool enabled);
}