using System.Reflection;

using AwesomeAssertions;

using Kora.Application.ViewModels;
using Kora.Application.Voice;
using Kora.Core.Platform;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MicrophoneRefreshOwnershipCompletionCannotDisposeSourceWhileHostCancellationOwnsIt(bool privateLoss)
    {
        var token = TestContext.Current.CancellationToken;
        var pending = new TaskCompletionSource<MicrophoneCatalogSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = new BoundedMicrophoneCatalog(() => pending.Task, TimeProvider.System, NullLogger.Instance);
        var fixture = CreateVoicePrivacyFixture(microphoneCatalog: catalog);
        await fixture.ViewModel.InitializeAsync();
        var refresh = fixture.ViewModel.RefreshMicrophonesAsync();
        var source = OwnedMicrophoneRefresh(fixture.ViewModel)!;
        var cancellationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCancellation = new ManualResetEventSlim();
        using var registration = source.Token.Register(() =>
        {
            cancellationEntered.SetResult();
            releaseCancellation.Wait(token);
        });
        if (privateLoss)
        {
            var current = fixture.PrivacyObservation.Current;
            fixture.PrivacyObservation.Publish(new(current, current with { TopologyRevision = current.TopologyRevision + 1 },
                WindowsPrivacyChangeReason.DeviceTopology));
            fixture.ViewModel.MicrophoneRefreshTask.Should().BeSameAs(refresh);
        }
        var disposal = Task.Run(fixture.ViewModel.Dispose, token);
        try
        {
            await cancellationEntered.Task.WaitAsync(token);
            pending.SetResult(TraySnapshot());
            await refresh.WaitAsync(token);
            OwnedMicrophoneRefresh(fixture.ViewModel).Should().BeNull();
            var observeOwnedToken = () => source.Token;
            observeOwnedToken.Should().NotThrow("Dispose owns cancellation until its callback has returned");
            fixture.ViewModel.IsRefreshingMicrophones.Should().BeFalse();
            fixture.ViewModel.IsMicrophoneCatalogCurrent.Should().BeFalse();
            fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        }
        finally
        {
            releaseCancellation.Set();
            await disposal;
        }
        var observeRetiredToken = () => source.Token;
        observeRetiredToken.Should().Throw<ObjectDisposedException>();
        await fixture.ViewModel.RefreshMicrophonesAsync();
        OwnedMicrophoneRefresh(fixture.ViewModel).Should().BeNull();
    }

    [Fact]
    public async Task MicrophoneRefreshOwnershipPublishesReplacementTaskBeforeReentrantCompletionAndCancelsOnlyItsSource()
    {
        var pending = new TaskCompletionSource<MicrophoneCatalogSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var catalog = new BoundedMicrophoneCatalog(() => ++calls == 1 ? Task.FromResult(TraySnapshot()) : pending.Task,
            TimeProvider.System, NullLogger.Instance);
        var fixture = CreateVoicePrivacyFixture(microphoneCatalog: catalog);
        await fixture.ViewModel.InitializeAsync();
        Task? replacement = null;
        CancellationTokenSource? originalSource = null;
        var replace = true;
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            if (!string.Equals(args.PropertyName, nameof(MainViewModel.IsRefreshingMicrophones), StringComparison.Ordinal)) { return; }
            if (fixture.ViewModel.IsRefreshingMicrophones && calls == 0)
            { originalSource = OwnedMicrophoneRefresh(fixture.ViewModel); }
            if (fixture.ViewModel.IsRefreshingMicrophones || !replace) { return; }
            replace = false;
            replacement = fixture.ViewModel.RefreshMicrophonesAsync();
        };
        await fixture.ViewModel.RefreshMicrophonesAsync();
        try
        {
            replacement.Should().NotBeNull();
            replacement!.IsCompleted.Should().BeFalse();
            fixture.ViewModel.RefreshMicrophonesAsync().Should().BeSameAs(replacement);
            fixture.ViewModel.MicrophoneRefreshTask.Should().BeSameAs(replacement);
            var replacementSource = OwnedMicrophoneRefresh(fixture.ViewModel);
            replacementSource.Should().NotBeNull().And.NotBeSameAs(originalSource);
            var observeOriginalToken = () => originalSource!.Token;
            observeOriginalToken.Should().Throw<ObjectDisposedException>();
            calls.Should().Be(2);
        }
        finally
        {
            fixture.ViewModel.Dispose();
            pending.SetResult(TraySnapshot());
            await replacement!;
        }
        OwnedMicrophoneRefresh(fixture.ViewModel).Should().BeNull();
        fixture.ViewModel.IsRefreshingMicrophones.Should().BeFalse();
        fixture.ViewModel.IsMicrophoneCatalogCurrent.Should().BeFalse();
    }

    private static CancellationTokenSource? OwnedMicrophoneRefresh(MainViewModel model) =>
        (CancellationTokenSource?)typeof(MainViewModel).GetField("microphoneRefreshCancellation",
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(model);
}
