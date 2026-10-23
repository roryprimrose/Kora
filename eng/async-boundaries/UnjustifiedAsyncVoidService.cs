namespace Kora.Application.AnalysisFixtures;

internal sealed class UnjustifiedAsyncVoidService
{
    internal async void Execute()
    {
        await Task.Yield();
    }
}
