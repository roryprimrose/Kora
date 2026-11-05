using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;

namespace Kora.Windows.IntegrationTests.NativeUx;

public sealed partial class NativeUxFixtureContractTests
{
    [Fact]
    public Task DisposalJoinsPendingPreferenceWriteBeforeClosingEvidenceAndDeletingStorage() =>
        HeadlessSession.RunAsync(async () =>
        {
            var token = TestContext.Current.CancellationToken;
            using var pending = new PendingPreferenceWrite(token);
            await using var fixture = new NativeUxFixtureSession(Path.GetTempPath(), appearanceStoreFactory: pending.CreateStore);
            await fixture.InitializeAsync();
            using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                HostActivityLayer.Application, HostOperation.Storage);
            var write = fixture.Main.SetResponseWindowPositionAsync(new ResponseWindowPosition(23, 45));
            try
            {
                (await Task.WhenAny(write, pending.Entered.Task).WaitAsync(token)).Should().BeSameAs(pending.Entered.Task,
                    "the admitted preference must enter the controlled writer before shutdown");
                await pending.Entered.Task.WaitAsync(token);
                File.Exists(pending.TemporaryPath).Should().BeTrue();
                var disposal = fixture.DisposeAsync().AsTask();
                disposal.IsCompleted.Should().BeFalse("the preference writer still owns an exclusive temporary file");
                Directory.Exists(fixture.LocalRoot).Should().BeTrue();
                pending.Release();
                await disposal.WaitAsync(token);
                (await write.WaitAsync(token)).Should().BeFalse("disposed input cannot publish a late preference confirmation");
                Directory.Exists(fixture.LocalRoot).Should().BeFalse();
                await fixture.DisposeAsync();
                fixture.Dispose();
                Directory.Exists(fixture.LocalRoot).Should().BeFalse();
                activity.Complete(HostOperationOutcome.Completed);
            }
            finally
            {
                pending.Release();
                await write.WaitAsync(token);
            }
        });

    private sealed class PendingPreferenceWrite(CancellationToken token) : IPreferenceStore, IDisposable
    {
        private readonly ManualResetEventSlim released = new();
        private IPreferenceStore? inner;
        private string? directory;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string TemporaryPath => Path.Combine(directory
            ?? throw new InvalidOperationException("The preference store is not initialized."), ".response-window-held.tmp");
        private IPreferenceStore Store => inner
            ?? throw new InvalidOperationException("The preference store is not initialized.");

        internal IPreferenceStore CreateStore(IApplicationDataPaths paths)
        {
            if (inner is not null) { throw new InvalidOperationException("The preference store is already initialized."); }
            directory = Path.Combine(paths.LocalRoot, "Preferences");
            inner = new LocalPreferenceStore(paths);
            return this;
        }

        public string? ReadText(string fileName) => Store.ReadText(fileName);
        public string[]? ReadLines(string fileName) => Store.ReadLines(fileName);
        public void WriteText(string fileName, string contents) => Store.WriteText(fileName, contents);
        public void Delete(string fileName) => Store.Delete(fileName);
        public void WriteLines(string fileName, IEnumerable<string> contents)
        {
            if (!string.Equals(fileName, "response-window.txt", StringComparison.Ordinal))
            {
                Store.WriteLines(fileName, contents);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(TemporaryPath)!);
            try
            {
                using var held = new FileStream(TemporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                Entered.TrySetResult();
                released.Wait(token);
                Store.WriteLines(fileName, contents);
            }
            finally { File.Delete(TemporaryPath); }
        }

        internal void Release() => released.Set();
        public void Dispose() => released.Dispose();
    }
}
