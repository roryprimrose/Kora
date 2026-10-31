using Avalonia.Headless;

using Xunit;

namespace Kora.Windows.IntegrationTests;

/// <summary>
/// Dispatches headless-window construction and interaction onto the single Avalonia UI thread created
/// for this test assembly, reused across the in-scope accessibility measurement tests below.
/// </summary>
internal static class HeadlessSession
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(HeadlessSession).Assembly);

    internal static Task RunAsync(Action action) => RunAsync(() => { action(); return Task.CompletedTask; });

    internal static Task RunAsync(Func<Task> action)
    {
        var timing = NativeFixturePhaseTimings.Capture(TestContext.Current);
        return Session.Dispatch<bool>(() => timing.RunAsync(async () =>
        {
            await action();
            return true;
        }), CancellationToken.None);
    }

    internal static Task<T> RunAsync<T>(Func<T> action)
    {
        var timing = NativeFixturePhaseTimings.Capture(TestContext.Current);
        return Session.Dispatch<T>(() => timing.RunAsync(() => Task.FromResult(action())), CancellationToken.None);
    }
}
