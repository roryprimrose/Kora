using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.LogicalTree;

using Kora.Controls;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Kora.NativeUxFixture;

internal static partial class NativePresenceFixture
{
    private sealed partial class FixtureWindow
    {
        private readonly List<RevealSample> revealSamples = [];

        private async Task VerifyTrayRevealAsync(NativeUxFixtureSession fixture, MainWindow window,
            bool native, DelayedEvidenceCommit delayed)
        {
            using var tray = new SystemTrayController(fixture.Main,
                fixture.Loggers.CreateLogger<SystemTrayController>());
            window.Hide();
            await Task.Delay(20, fixture.Token);
            delayed.Arm();
            try
            {
                var started = Stopwatch.GetTimestamp();
                tray.HandlePrimaryClick();
                var shown = Stopwatch.GetElapsedTime(started);
                Require(window.IsVisible && window.Opacity == 1 && shown < TimeSpan.FromSeconds(1),
                    $"First tray click did not reveal an opaque shell under one second: {shown.TotalMilliseconds:F1} ms, opacity {window.Opacity:F1}.");
                var cloud = window.GetLogicalDescendants().OfType<PresenceControl>().Single();
                var nativeHost = window.GetLogicalDescendants().OfType<NativePresenceHost>().SingleOrDefault();
                while (!(native ? nativeHost?.CanInteract == true : cloud.CanInteract))
                {
                    Require(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(1),
                        "The first positive-opacity presence frame missed its one-second deadline.");
                    await Task.Delay(10, fixture.Token);
                }
                var frame = Stopwatch.GetElapsedTime(started);
                await delayed.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), fixture.Token);
                Require(!delayed.Release.IsSet, "The slow SQLite commit was released before the visual deadline check.");
                revealSamples.Add(new(native ? "native" : "avalonia", shown.TotalMilliseconds, frame.TotalMilliseconds,
                    frame < TimeSpan.FromMilliseconds(500)));
                Console.WriteLine($"Synthetic tray reveal ({(native ? "native" : "avalonia")}): shell {shown.TotalMilliseconds:F1} ms, "
                    + $"first positive-opacity frame {frame.TotalMilliseconds:F1} ms, SQLite still blocked.");
            }
            finally
            {
                delayed.Release.Set();
                if (delayed.Entered.Task.IsCompleted)
                {
                    await delayed.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5), fixture.Token);
                }
            }
        }

        private sealed record RevealSample(string Renderer, double ShellMilliseconds,
            double FirstPositiveFrameMilliseconds, bool PreferredHalfSecondMet);
    }

    private sealed class DelayedEvidenceCommit : ISqliteTransactionCheckpoint, IDisposable
    {
        private int armed;
        internal TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Completed { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new();
        internal void Arm()
        {
            Release.Reset();
            Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref armed, 1);
        }
        public void BeforeWrite(SqliteConnection connection, SqliteTransaction transaction) { }
        public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction)
        {
            if (Interlocked.Exchange(ref armed, 0) == 0) { return; }
            Entered.TrySetResult();
            if (!Release.Wait(TimeSpan.FromSeconds(15)))
            {
                throw new IOException("Synthetic reveal commit was not released before its bound.");
            }
            Completed.TrySetResult();
        }
        public void Dispose()
        {
            Release.Set();
            Release.Dispose();
        }
    }
}
