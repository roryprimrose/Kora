using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.LogicalTree;

using Kora.Controls;

namespace Kora.NativeUxFixture;

internal static partial class NativePresenceFixture
{
    private sealed partial class FixtureWindow
    {
        private static double PresenceOpacity(MainWindow window) =>
            window.GetLogicalDescendants().OfType<NativePresenceHost>().SingleOrDefault()?.PresentedOpacity
                ?? window.GetLogicalDescendants().OfType<PresenceControl>().Single().CurrentFrame.Opacity;

        private static async Task WaitForFullOpacityAsync(MainWindow window, CancellationToken token)
        {
            var started = Stopwatch.GetTimestamp();
            while (PresenceOpacity(window) < 1)
            {
                Require(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(1),
                    "The presence did not finish fading in within one second.");
                await Task.Delay(10, token);
            }
        }

        private static async Task VerifyFadeCancellationAsync(PresenceWindowController controller,
            MainWindow window, CancellationToken token)
        {
            await WaitForFullOpacityAsync(window, token);
            var active = controller.ActiveNative;
            Require(!controller.PrepareToApply(), "The visible outgoing renderer was retired without a fade.");
            var started = Stopwatch.GetTimestamp();
            while (PresenceOpacity(window) is 0 or 1)
            {
                Require(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(1),
                    "The outgoing renderer did not produce an intermediate-opacity frame before cancellation.");
                await Task.Delay(10, token);
            }
            controller.Request(active);
            Require(controller.TryApply() && window.IsVisible && IsWindow(window.TryGetPlatformHandle()!.Handle),
                "Cancelling the renderer choice did not retain the existing visible window.");
            Require(!controller.HasPendingChange && controller.ActiveNative == active,
                "Cancelling a renderer choice left pending work or changed renderer.");
            await WaitForFullOpacityAsync(window, token);
            controller.Request(!active);
        }

        private static async Task<double> VerifyFadeOutAsync(PresenceWindowController controller,
            MainWindow window, CancellationToken token)
        {
            if (!window.IsVisible || window.WindowState == WindowState.Minimized)
            {
                Require(controller.PrepareToApply(), "Hidden or minimized replacement waited for a cosmetic fade.");
                return 0;
            }
            await WaitForFullOpacityAsync(window, token);
            var handle = window.TryGetPlatformHandle()!.Handle;
            var started = Stopwatch.GetTimestamp();
            var intermediate = false;
            Require(!controller.PrepareToApply(), "The outgoing renderer did not begin a staged fade.");
            while (!controller.PrepareToApply())
            {
                intermediate |= PresenceOpacity(window) is > 0 and < 1;
                Require(IsWindow(handle) && window.IsVisible && controller.IsAvailable,
                    "The outgoing owner was retired or hidden before its fade completed.");
                Require(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(1),
                    "The outgoing visibility fade exceeded one second.");
                await Task.Delay(10, token);
            }
            Require(intermediate && PresenceOpacity(window) == 0 && IsWindow(handle),
                "Replacement was admitted without an intermediate fade and a final zero-opacity frame.");
            return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }

        private static async Task<double> VerifyFadeInAsync(MainWindow window, CancellationToken token)
        {
            var initialOpacity = PresenceOpacity(window);
            Console.WriteLine($"Incoming renderer opacity at controller return: {initialOpacity:R}.");
            Require(initialOpacity is >= 0 and < 1,
                $"A replacement hard-switched to full opacity: {initialOpacity:R}.");
            var started = Stopwatch.GetTimestamp();
            var intermediate = false;
            while (PresenceOpacity(window) < 1)
            {
                intermediate |= PresenceOpacity(window) is > 0 and < 1;
                Require(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(1),
                    "The incoming visibility fade exceeded one second.");
                await Task.Delay(10, token);
            }
            Require(intermediate, "The incoming renderer hard-switched to full opacity.");
            return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }

        private static async Task VerifyPrivacyAndStopDuringFadeAsync(NativeUxFixtureSession fixture,
            PresenceWindowController controller, MainWindow window)
        {
            await WaitForFullOpacityAsync(window, fixture.Token);
            var active = controller.ActiveNative;
            var handle = window.TryGetPlatformHandle()!.Handle;
            controller.Request(!active);
            Require(!controller.PrepareToApply(), "The privacy test did not begin an outgoing fade.");
            fixture.Access.SetOpen(false);
            var started = Stopwatch.GetTimestamp();
            while (fixture.Main.CanRevealPrivatePresentation)
            {
                Require(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(1),
                    "Synthetic privacy closure was not observed.");
                await Task.Delay(10, fixture.Token);
            }
            controller.Request(active);
            Require(controller.TryApply() && !controller.HasPendingChange && IsWindow(handle),
                "Privacy-time cancellation retired or replaced the current owner.");
            Require(!window.GetLogicalDescendants().OfType<PresenceControl>().Single().CanInteract
                && window.GetLogicalDescendants().OfType<NativePresenceHost>().All(control => !control.CanInteract),
                "Cancelling a fade revived private presentation or input.");
            fixture.Access.SetOpen(true);
            fixture.Main.ShowApplication();
            await WaitForFullOpacityAsync(window, fixture.Token);
            controller.Request(!active);
            Require(!controller.PrepareToApply(), "The shutdown test did not begin an outgoing fade.");
            controller.Stop();
            Require(controller.TryApply() && !controller.HasPendingChange && !controller.IsAvailable && IsWindow(handle),
                "Stopping during an outgoing fade retired or replaced a window after shutdown.");
        }
    }
}
