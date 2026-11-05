using System.Collections.Concurrent;
using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Voice;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Voice;

public sealed class SpeechPreparationCoordinatorTests
{
    [Fact]
    public async Task Only_selected_Kokoro_voice_is_prepared_and_repeated_requests_are_coalesced()
    {
        var service = new PreparationService();
        using var coordinator = Create(service);
        var notifications = new ConcurrentQueue<string>();
        coordinator.Changed += (_, _) => notifications.Enqueue(coordinator.Status);
        coordinator.Request(new("windows", "Windows", "en-US", SpeechVoiceGender.Female), static () => true);
        await coordinator.WaitForQuiescenceAsync();
        service.Voices.Should().BeEmpty();
        coordinator.Request(Voice("af_heart"), static () => true);
        await coordinator.WaitForQuiescenceAsync();
        coordinator.Request(Voice("af_heart"), static () => true);
        await coordinator.WaitForQuiescenceAsync();
        coordinator.Request(Voice("af_sky"), static () => true);
        await coordinator.WaitForQuiescenceAsync();

        service.Voices.Should().Equal("af_heart", "af_sky");
        coordinator.Status.Should().Be("Selected speech voice is prepared.");
        coordinator.IsQuiescent.Should().BeTrue();
        notifications.Should().HaveCount(4);
        notifications.Last().Should().Be("Selected speech voice is prepared.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unknown_or_ineligible_host_does_not_start_preparation(bool unknown)
    {
        var service = new PreparationService();
        using var coordinator = Create(service);

        coordinator.Request(Voice("af_heart"), unknown ? null : static () => false);
        await coordinator.WaitForQuiescenceAsync();

        service.Voices.Should().BeEmpty();
        coordinator.Status.Should().BeEmpty();
    }

    [Fact]
    public async Task Latest_voice_waits_for_native_work_and_skips_retired_queued_selection()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new PreparationService
        {
            Prepare = async (voice, _) =>
            {
                if (string.Equals(voice.Id, "af_heart", StringComparison.Ordinal))
                {
                    started.SetResult();
                    await release.Task;
                }
            },
        };
        using var coordinator = Create(service);
        coordinator.Request(Voice("af_heart"), static () => true);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        coordinator.Request(Voice("af_sky"), static () => true);
        coordinator.Request(Voice("bf_emma"), static () => true);
        try
        {
            coordinator.IsQuiescent.Should().BeFalse();
            service.Voices.Should().Equal("af_heart");
        }
        finally { release.SetResult(); }
        await coordinator.WaitForQuiescenceAsync();

        service.Voices.Should().Equal("af_heart", "bf_emma");
        coordinator.Status.Should().Be("Selected speech voice is prepared.");
    }

    [Fact]
    public async Task Withdrawal_or_disposal_retires_results_and_waits_for_native_quiescence()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new PreparationService { Prepare = (_, _) => { started.SetResult(); return release.Task; } };
        using var coordinator = Create(service);
        coordinator.Request(Voice("af_heart"), static () => true);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        coordinator.Request(null);
        var notifications = 0;
        coordinator.Changed += (_, _) => notifications++;
        coordinator.Dispose();
        coordinator.Request(Voice("af_sky"), static () => true);
        try
        {
            coordinator.IsQuiescent.Should().BeFalse();
            coordinator.WaitForQuiescenceAsync().IsCompleted.Should().BeFalse();
        }
        finally { release.SetResult(); }
        await coordinator.WaitForQuiescenceAsync();

        notifications.Should().Be(0);
        coordinator.Status.Should().BeEmpty();
        coordinator.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task Failure_is_explicit_and_same_voice_can_be_retried()
    {
        var fail = true;
        var failure = new IOException("Asset unavailable.");
        var logger = new FailureLogger();
        var service = new PreparationService
        {
            Prepare = (_, _) => fail ? Task.FromException(failure) : Task.CompletedTask,
        };
        using var coordinator = new SpeechPreparationCoordinator(service, logger);
        coordinator.Request(Voice("af_heart"), static () => true);
        await coordinator.WaitForQuiescenceAsync();
        coordinator.Status.Should().Contain("failed");
        logger.Failure.Should().BeSameAs(failure);
        fail = false;
        coordinator.Request(Voice("af_heart"), static () => true);
        await coordinator.WaitForQuiescenceAsync();
        service.Voices.Should().Equal("af_heart", "af_heart");
        coordinator.Status.Should().Be("Selected speech voice is prepared.");
    }

    [Fact]
    public async Task Eligibility_lost_during_native_preparation_cannot_publish_readiness()
    {
        var eligible = true;
        var service = new PreparationService
        {
            Prepare = (_, _) =>
            {
                eligible = false;
                return Task.CompletedTask;
            },
        };
        using var coordinator = Create(service);
        coordinator.Request(Voice("af_heart"), () => eligible);
        await coordinator.WaitForQuiescenceAsync();

        service.Voices.Should().Equal("af_heart");
        coordinator.Status.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithdrawalReturnsWithoutCallbacksAndJoinsBothOwners(bool dispose)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nativeReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseNative = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCallback = new ManualResetEventSlim();
        CancellationTokenRegistration registration = default;
        var service = new PreparationService
        {
            Prepare = async (_, token) =>
            {
                registration = token.Register(() =>
                {
                    callbackStarted.SetResult();
                    releaseCallback.Wait(TestContext.Current.CancellationToken);
                });
                started.SetResult();
                await releaseNative.Task;
                nativeReleased.SetResult();
            },
        };
        using var coordinator = Create(service);
        coordinator.Request(Voice("af_heart"), static () => true);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        var withdrawal = Task.Run(() =>
        {
            if (dispose) { coordinator.Dispose(); }
            else { coordinator.Request(null); }
        }, TestContext.Current.CancellationToken);
        await callbackStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            await withdrawal.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            coordinator.Status.Should().BeEmpty();
            coordinator.IsQuiescent.Should().BeFalse();
            releaseNative.SetResult();
            await nativeReleased.Task.WaitAsync(TestContext.Current.CancellationToken);
            coordinator.WaitForQuiescenceAsync().IsCompleted.Should().BeFalse();
            coordinator.IsQuiescent.Should().BeFalse();
            service.Voices.Should().Equal("af_heart");
        }
        finally
        {
            releaseNative.TrySetResult();
            releaseCallback.Set();
            await withdrawal;
            await coordinator.WaitForQuiescenceAsync();
            await registration.DisposeAsync();
        }
        coordinator.IsQuiescent.Should().BeTrue();
        coordinator.Status.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallbackFailureCannotInterruptRetirementOrPublishReadiness(bool dispose)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new InvalidOperationException("Cancellation callback failed.");
        CancellationTokenRegistration registration = default;
        var logger = new FailureLogger();
        var service = new PreparationService
        {
            Prepare = (_, token) =>
            {
                registration = token.Register(() => throw failure);
                started.SetResult();
                return release.Task;
            },
        };
        using var coordinator = new SpeechPreparationCoordinator(service, logger);
        coordinator.Request(Voice("af_heart"), static () => true);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            Action retire = () =>
            {
                if (dispose) { coordinator.Dispose(); }
                else { coordinator.Request(null); }
            };
            retire.Should().NotThrow();
            coordinator.IsQuiescent.Should().BeFalse();
            coordinator.WaitForQuiescenceAsync().IsCompleted.Should().BeFalse();
        }
        finally
        {
            release.TrySetResult();
            await coordinator.WaitForQuiescenceAsync();
            await registration.DisposeAsync();
        }
        logger.Failure.Should().BeOfType<AggregateException>().Which.InnerExceptions.Should().Contain(failure);
        coordinator.Status.Should().NotContain("is prepared");
        if (!dispose) { coordinator.Status.Should().Contain("cancellation failed"); }
        service.Voices.Should().Equal("af_heart");
        coordinator.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacementWaitsForRetiredCallbacksAndPreservesItsOwnResult(bool fail)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nativeReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseNative = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCallback = new ManualResetEventSlim();
        CancellationTokenRegistration registration = default;
        var logger = new FailureLogger();
        var service = new PreparationService
        {
            Prepare = async (voice, token) =>
            {
                if (!string.Equals(voice.Id, "af_heart", StringComparison.Ordinal)) { return; }
                registration = token.Register(() =>
                {
                    callbackStarted.SetResult();
                    releaseCallback.Wait(TestContext.Current.CancellationToken);
                    if (fail) { throw new InvalidOperationException("Cancellation callback failed."); }
                });
                started.SetResult();
                await releaseNative.Task;
                nativeReleased.SetResult();
            },
        };
        using var coordinator = new SpeechPreparationCoordinator(service, logger);
        coordinator.Request(Voice("af_heart"), static () => true);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        var replacement = Task.Run(() => coordinator.Request(Voice("af_sky"), static () => true),
            TestContext.Current.CancellationToken);
        await callbackStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            await replacement.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            releaseNative.SetResult();
            await nativeReleased.Task.WaitAsync(TestContext.Current.CancellationToken);
            coordinator.Status.Should().Be("Preparing selected speech voice...");
            coordinator.IsQuiescent.Should().BeFalse();
            service.Voices.Should().Equal("af_heart");
        }
        finally
        {
            releaseNative.TrySetResult();
            releaseCallback.Set();
            await replacement;
            await coordinator.WaitForQuiescenceAsync();
            await registration.DisposeAsync();
        }
        service.Voices.Should().Equal("af_heart", "af_sky");
        coordinator.Status.Should().Be("Selected speech voice is prepared.");
        (logger.Failure is AggregateException).Should().Be(fail);
        coordinator.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task FatalPreparationFailurePropagatesAfterOwnedRetirement()
    {
#pragma warning disable CA2201 // Inject the fatal boundary deterministically without inducing real memory pressure.
        var failure = new OutOfMemoryException("Simulated fatal preparation failure.");
#pragma warning restore CA2201
        var logger = new FailureLogger();
        var service = new PreparationService { Prepare = (_, _) => Task.FromException(failure) };
        using var coordinator = new SpeechPreparationCoordinator(service, logger);
        coordinator.Request(Voice("af_heart"), static () => true);

        Func<Task> join = coordinator.WaitForQuiescenceAsync;
        (await join.Should().ThrowAsync<OutOfMemoryException>()).Which.Should().BeSameAs(failure);
        coordinator.IsQuiescent.Should().BeTrue();
        coordinator.Status.Should().BeEmpty();
        logger.Failure.Should().BeNull();
        service.Voices.Should().Equal("af_heart");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deferred_preparation_links_to_original_host_context_and_ends_truthfully(bool fail)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        Activity? preparation = null;
        HostRequest? observed = null;
        var service = new PreparationService
        {
            Prepare = (_, _) =>
            {
                preparation = Activity.Current;
                observed = HostActivity.RequireCurrent().Request;
                return fail ? Task.FromException(new IOException("Preparation failed.")) : Task.CompletedTask;
            },
        };
        using var coordinator = Create(service);
        HostRequest expected;
        ActivityContext cause;
        using (var activity = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Presentation))
        {
            expected = activity.Request;
            cause = activity.Activity!.Context;
            coordinator.Request(Voice("af_heart"), static () => true);
            activity.Complete(HostOperationOutcome.Completed);
        }
        await coordinator.WaitForQuiescenceAsync();

        observed.Should().Be(expected);
        preparation.Should().NotBeNull();
        preparation!.ParentId.Should().BeNull();
        preparation.Links.Should().ContainSingle().Which.Context.Should().Be(cause);
        preparation.Status.Should().Be(fail ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
        preparation.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    private static SpeechPreparationCoordinator Create(PreparationService service) =>
        new(service, NullLogger<SpeechPreparationCoordinator>.Instance);

    private static SpeechVoice Voice(string id) =>
        new(id, id, "en-US", SpeechVoiceGender.Female) { ProviderId = SpeechProviderIds.Kokoro };

    private sealed class PreparationService : ISpeechPreparationService
    {
        public ConcurrentQueue<string> Voices { get; } = new();
        public Func<SpeechVoice, CancellationToken, Task> Prepare { get; init; } = (_, _) => Task.CompletedTask;

        public Task PrepareAsync(SpeechVoice voice, CancellationToken cancellationToken = default)
        {
            Voices.Enqueue(voice.Id);
            return Prepare(voice, cancellationToken);
        }
    }

    private sealed class FailureLogger : ILogger<SpeechPreparationCoordinator>
    {
        public Exception? Failure { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) => Failure = exception;
    }
}
