using Microsoft.Extensions.Logging;

namespace Kora.Windows.Audio;

public sealed partial class KokoroTextToSpeechProvider
{
    [LoggerMessage(209, LogLevel.Information,
        "Kokoro {Phase} finished in {ElapsedMilliseconds} ms; succeeded: {Succeeded}.")]
    private partial void PhaseFinished(string phase, double elapsedMilliseconds, bool succeeded);
}
