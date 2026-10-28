namespace Kora.Windows.Audio;

internal interface IHandsFreeCommandEndpoint : IDisposable
{
    bool Accept(ReadOnlySpan<short> audio);
}
