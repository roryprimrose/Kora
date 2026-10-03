namespace Kora.Core.Voice;

public interface IResponseOutputPreferences
{
    ResponseOutputMode? LoadDefaultMode();

    void SaveDefaultMode(ResponseOutputMode mode);
}