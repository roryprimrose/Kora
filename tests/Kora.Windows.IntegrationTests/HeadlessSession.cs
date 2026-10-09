using Avalonia.Headless;

namespace Kora.Windows.IntegrationTests;

/// <summary>
/// Dispatches headless-window construction and interaction onto the single Avalonia UI thread created
/// for this test assembly, reused across the in-scope accessibility measurement tests below.
/// </summary>
internal static class HeadlessSession
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(HeadlessSession).Assembly);

    internal static Task RunAsync(Action action) => Session.Dispatch(action, CancellationToken.None);

    internal static Task RunAsync(Func<Task> action) => Session.Dispatch<bool>(async () =>
    {
        await action();
        return true;
    }, CancellationToken.None);

    internal static Task<T> RunAsync<T>(Func<T> action) => Session.Dispatch(action, CancellationToken.None);
}
