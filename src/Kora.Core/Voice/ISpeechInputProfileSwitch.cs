namespace Kora.Core.Voice;

/// <summary>An exclusive quiescent cutover. Disposing never starts capture.</summary>
public interface ISpeechInputProfileSwitch : IAsyncDisposable
{
    /// <summary>Publishes a validated generation while capture remains blocked by this lease.</summary>
    void Publish(SpeechInputGeneration generation);
    /// <summary>Holds capture unavailable without advancing the committed configuration generation.</summary>
    void HoldUnavailable();
}
