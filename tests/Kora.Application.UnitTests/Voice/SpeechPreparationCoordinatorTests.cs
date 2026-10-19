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
