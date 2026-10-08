namespace Kora.NativeUxFixture;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => NativeUxFixtureHost.Run(args);
}
