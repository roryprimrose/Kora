namespace Kora.Core.Voice;

public interface IResponseOutputPreferences
{
    ResponseOutputMode? LoadDefaultMode();

    void SaveDefaultMode(ResponseOutputMode mode);

    ResponseOutputMode? ReadBackDefaultMode();

    void BeginDefaultModeWrite();

    void ConfirmDefaultModeWrite();

    bool? LoadMutedOutputVisualFallback();

    void SaveMutedOutputVisualFallback(bool enabled);
}