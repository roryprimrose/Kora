namespace Kora.Windows.Audio;

internal interface IHandsFreeMicrophone : IAsyncDisposable
{
    event HandsFreeSamplesAvailable? Samples;
    event EventHandler? Failed;
    void Start();
}
