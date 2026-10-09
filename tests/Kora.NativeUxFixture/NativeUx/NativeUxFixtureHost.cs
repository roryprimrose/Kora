using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using Kora;

namespace Kora.NativeUxFixture;

public static class NativeUxFixtureHost
{
    public static int Run(string[] args)
    {
        try
        {
            if (!TryGetScratchParent(args, out var scratchParent))
            {
                Console.Error.WriteLine("Launch denied: explicit approval and --launch-native-fixtures --scratch-parent "
                    + "<existing absolute directory on a fixed local drive> are required. UNC, mapped-drive and reparse-point parents are not accepted.");
                return 2;
            }
            using var session = new NativeUxFixtureSession(scratchParent);
            using var clipboard = new NativeUxClipboardGuard();
            Console.WriteLine($"Synthetic native UX fixture. PID: {Environment.ProcessId}. Scratch: {session.LocalRoot}");
            Console.WriteLine("No production startup, instance handoff, audio, model, network, browser, shared clipboard or installed artifact discovery.");
            return AppBuilder.Configure(() => new FixtureApplication(session))
                .UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime([]);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    internal static bool TryGetScratchParent(string[] args, out string scratchParent)
    {
        scratchParent = string.Empty;
        if (args.Length != 3 || !string.Equals(args[0], "--launch-native-fixtures", StringComparison.Ordinal)
            || !string.Equals(args[1], "--scratch-parent", StringComparison.Ordinal)
            || !IsLocalScratchParent(args[2]))
        {
            return false;
        }
        scratchParent = Path.GetFullPath(args[2]);
        return true;
    }

    internal static bool IsLocalScratchParent(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':')
        {
            return false;
        }
        var root = Path.GetPathRoot(path)!;
        return new DriveInfo(root).DriveType == DriveType.Fixed && Directory.Exists(path)
            && !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
    }

    internal static void LoadPresentation(Avalonia.Application target)
    {
        // Load the production XAML only, never its framework-completion callback or service composition.
        var presentation = new App();
        presentation.Initialize();
        var resources = presentation.Resources;
        var styles = presentation.Styles.ToArray();
        presentation.Resources = new ResourceDictionary();
        presentation.Styles.Clear();
        target.Resources = resources;
        foreach (var style in styles) { target.Styles.Add(style); }
    }

    private sealed class FixtureApplication(NativeUxFixtureSession session) : Avalonia.Application
    {
        public override void Initialize() => LoadPresentation(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            {
                throw new InvalidOperationException("A native desktop fixture lifetime is required.");
            }
            desktop.MainWindow = new NativeUxFixtureWindow(session, desktop);
            base.OnFrameworkInitializationCompleted();
        }
    }
}
