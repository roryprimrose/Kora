using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Headless;

using AwesomeAssertions;

using Kora.NativeUxFixture;

using NativeFixturePhase = Kora.NativeUxFixture.NativeFixturePhaseScope.PhaseKind;

namespace Kora.Windows.IntegrationTests.NativeUx;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class NativeFixturePhaseTimingTests
{
    [Fact]
    public void ExactTypedShapeUsesOnlyAllowlistedMetadataAndMonotonicElapsedTime()
    {
        var clock = new PhaseClock();
        var reports = new List<string>();
        var timing = new NativeFixturePhaseTimings("original-test-id", clock, reports.Add);
        using (var phase = new NativeFixturePhaseScope(NativeFixturePhase.OwnedStorage, timing.Observe))
        {
            clock.Advance(7);
            clock.UtcNow = clock.UtcNow.AddDays(-2);
            phase.Complete();
        }
        using var report = JsonDocument.Parse(reports[^1]);
        report.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal("Schema", "TestUniqueId", "Timing", "PhaseStates");
        report.RootElement.GetProperty("Schema").GetInt32().Should().Be(1);
        report.RootElement.GetProperty("TestUniqueId").GetString().Should().Be("original-test-id");
        var timingProperty = report.RootElement.GetProperty("Timing");
        timingProperty.EnumerateObject().Select(property => property.Name).Should().Equal("GlobalTiming", "StepTimings");
        var step = timingProperty.GetProperty("StepTimings").EnumerateArray().Should().ContainSingle().Subject;
        step.EnumerateObject().Select(property => property.Name).Should().Equal("Id", "Description", "Timing");
        step.GetProperty("Id").GetString().Should().Be("1");
        step.GetProperty("Description").GetString().Should().Be("owned-storage");
        var interval = step.GetProperty("Timing");
        interval.EnumerateObject().Select(property => property.Name).Should().Equal("StartTime", "EndTime", "Duration");
        interval.GetProperty("Duration").GetString().Should().Be("00:00:00.0070000");
        (interval.GetProperty("EndTime").GetDateTimeOffset() - interval.GetProperty("StartTime").GetDateTimeOffset())
            .Should().Be(TimeSpan.FromMilliseconds(7));
        var state = report.RootElement.GetProperty("PhaseStates").EnumerateArray().Should().ContainSingle().Subject;
        state.EnumerateObject().Select(property => property.Name).Should().Equal("Id", "Phase", "Outcome");
        state.GetProperty("Outcome").GetString().Should().Be("completed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureAndCancellationAreRethrownWithoutContentAndRunningIsNotTerminal(bool cancelled)
    {
        const string forbidden = "PRIVATE_AUDIO_TRANSCRIPT_RESPONSE_FILENAME_CREDENTIAL_SCOPE_AUDIT";
        var clock = new PhaseClock();
        var reports = new List<string>();
        var timing = new NativeFixturePhaseTimings("original-test-id", clock, reports.Add);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception failure = cancelled ? new OperationCanceledException(forbidden) : new IOException(forbidden);
        async Task Run()
        {
            using var phase = new NativeFixturePhaseScope(NativeFixturePhase.NativeUi, timing.Observe);
            await phase.RunAsync(async () =>
            {
                entered.SetResult();
                await release.Task;
                clock.Advance(11);
                throw failure;
            });
        }
        var operation = Run();
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        using (var running = JsonDocument.Parse(reports[^1]))
        {
            running.RootElement.GetProperty("Timing").GetProperty("StepTimings").GetArrayLength().Should().Be(0);
            running.RootElement.GetProperty("PhaseStates")[0].GetProperty("Outcome").GetString().Should().Be("running");
        }
        release.SetResult();
        var thrown = await Record.ExceptionAsync(() => operation);
        thrown.Should().BeSameAs(failure);
        using var terminal = JsonDocument.Parse(reports[^1]);
        terminal.RootElement.GetProperty("PhaseStates")[0].GetProperty("Outcome").GetString()
            .Should().Be(cancelled ? "cancelled" : "failed");
        terminal.RootElement.GetProperty("Timing").GetProperty("StepTimings")[0]
            .GetProperty("Timing").GetProperty("Duration").GetString().Should().Be("00:00:00.0110000");
        reports.Should().OnlyContain(json => !json.Contains(forbidden, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConcurrentRepeatedPhasesRetainDistinctIdsAndJoinedTerminalSnapshots()
    {
        var clock = new PhaseClock();
        var reports = new List<string>();
        var timing = new NativeFixturePhaseTimings("original-test-id", clock, reports.Add);
        var first = new NativeFixturePhaseScope(NativeFixturePhase.Join, timing.Observe);
        var second = new NativeFixturePhaseScope(NativeFixturePhase.Join, timing.Observe);
        clock.Advance(3);
        first.Complete();
        second.Complete();
        await Task.WhenAll(Task.Run(first.Dispose, TestContext.Current.CancellationToken),
            Task.Run(second.Dispose, TestContext.Current.CancellationToken));
        first.Dispose();
        second.Dispose();
        reports.Should().HaveCount(4);
        using var terminal = JsonDocument.Parse(reports[^1]);
        var steps = terminal.RootElement.GetProperty("Timing").GetProperty("StepTimings").EnumerateArray().ToArray();
        steps.Select(step => step.GetProperty("Id").GetString()).Should().Equal("1", "2");
        steps.Select(step => step.GetProperty("Description").GetString()).Should().Equal("join", "join");
        terminal.RootElement.GetProperty("PhaseStates").EnumerateArray()
            .Should().OnlyContain(state => state.GetProperty("Outcome").GetString() == "completed");
    }

    [Fact]
    public async Task ReportFailureDuringDisposalPreservesOriginalCallbackFailureAndPhysicalCleanup()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            var original = new IOException("Synthetic original callback failure");
            var publication = new IOException("Synthetic disposal report failure");
            string? root = null;
            var timing = new NativeFixturePhaseTimings("disposal-execution", new PhaseClock(), json =>
            {
                using var report = JsonDocument.Parse(json);
                if (report.RootElement.GetProperty("PhaseStates").EnumerateArray()
                    .Any(state => string.Equals(state.GetProperty("Phase").GetString(), "disposal", StringComparison.Ordinal)))
                {
                    throw publication;
                }
            });
            var thrown = await Record.ExceptionAsync(() => timing.RunAsync<bool>(() =>
            {
                using var fixture = new NativeUxFixtureSession(parent);
                root = fixture.LocalRoot;
                throw original;
            }));
            Directory.Exists(root).Should().BeFalse();
            var aggregate = thrown.Should().BeOfType<AggregateException>().Subject;
            aggregate.InnerExceptions.Should().Equal(original, publication);
        });
    }

    [Fact]
    public async Task ConcurrentOperationAndReportFailuresAreBothSurfacedAfterJoin()
    {
        var original = new OperationCanceledException("Synthetic original cancellation");
        var publication = new IOException("Synthetic report failure");
        var timing = new NativeFixturePhaseTimings("failed-execution", new PhaseClock(), _ => throw publication);
        var joined = false;
        var thrown = await Record.ExceptionAsync(() => timing.RunAsync<bool>(async () =>
        {
            try
            {
                await Task.Yield();
                throw original;
            }
            finally { joined = true; }
        }));
        joined.Should().BeTrue();
        thrown.Should().BeOfType<AggregateException>().Subject.InnerExceptions.Should().Equal(original, publication);
        NativeFixturePhaseTimings.Capture(TestContext.Current).Should().NotBeSameAs(timing);
    }

    [Fact]
    public async Task LateCallbackDoesNotReuseAnEndedAmbientOperation()
    {
        var timing = new NativeFixturePhaseTimings("ended-execution", new PhaseClock(), _ => { });
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<NativeFixturePhaseTimings>? callback = null;
        try
        {
            await timing.RunAsync(() =>
            {
                callback = Task.Run(async () =>
                {
                    await release.Task;
                    return NativeFixturePhaseTimings.Capture(TestContext.Current);
                }, TestContext.Current.CancellationToken);
                return Task.FromResult(true);
            });
        }
        finally { release.SetResult(); }
        (await callback!).Should().BeSameAs(NativeFixturePhaseTimings.Capture(TestContext.Current));
    }

    [Fact]
    public void NegativeTimestampOriginAndUtcOffsetPreserveOverlappingWallIntervals()
    {
        var clock = new PhaseClock { UtcNow = new(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-7)) };
        clock.Advance(-20);
        var origin = clock.UtcNow;
        var reports = new List<string>();
        var timing = new NativeFixturePhaseTimings("offset-execution", clock, reports.Add);
        using var first = new NativeFixturePhaseScope(NativeFixturePhase.Setup, timing.Observe);
        clock.Advance(3);
        using var second = new NativeFixturePhaseScope(NativeFixturePhase.Context, timing.Observe);
        clock.UtcNow = clock.UtcNow.AddDays(2);
        clock.Advance(5);
        first.Complete();
        first.Dispose();
        using (var partial = JsonDocument.Parse(reports[^1]))
        {
            partial.RootElement.GetProperty("Timing").GetProperty("StepTimings").GetArrayLength().Should().Be(1);
            partial.RootElement.GetProperty("PhaseStates")[1].GetProperty("Outcome").GetString().Should().Be("running");
        }
        clock.Advance(2);
        second.Complete();
        second.Dispose();
        using var terminal = JsonDocument.Parse(reports[^1]);
        var global = terminal.RootElement.GetProperty("Timing").GetProperty("GlobalTiming");
        global.GetProperty("StartTime").GetDateTimeOffset().Should().Be(origin);
        global.GetProperty("EndTime").GetDateTimeOffset().Should().Be(origin.AddMilliseconds(10));
        global.GetProperty("Duration").GetString().Should().Be("00:00:00.0100000");
        var steps = terminal.RootElement.GetProperty("Timing").GetProperty("StepTimings");
        steps[0].GetProperty("Timing").GetProperty("Duration").GetString().Should().Be("00:00:00.0080000");
        steps[1].GetProperty("Timing").GetProperty("StartTime").GetDateTimeOffset().Should().Be(origin.AddMilliseconds(3));
        steps[1].GetProperty("Timing").GetProperty("Duration").GetString().Should().Be("00:00:00.0070000");
    }

    [Fact]
    public async Task NestedOperationFailuresRestoreTheOuterOwnerWithoutDuplicatingPublicationFailure()
    {
        var original = new IOException("Synthetic nested callback failure");
        var publication = new IOException("Synthetic nested report failure");
        var owner = new NativeFixturePhaseTimings("nested-execution", new PhaseClock(), _ => throw publication);
        var thrown = await Record.ExceptionAsync(() => owner.RunAsync(() => owner.RunAsync<bool>(async () =>
        {
            await Task.Yield();
            NativeFixturePhaseTimings.Capture(TestContext.Current).Should().BeSameAs(owner);
            throw original;
        })));
        thrown.Should().BeOfType<AggregateException>().Subject.InnerExceptions.Should().Equal(original, publication);
        NativeFixturePhaseTimings.Capture(TestContext.Current).Should().NotBeSameAs(owner);

        var outer = new NativeFixturePhaseTimings("outer-execution", new PhaseClock(), _ => { });
        var inner = new NativeFixturePhaseTimings("inner-execution", new PhaseClock(), _ => { });
        await outer.RunAsync(async () =>
        {
            NativeFixturePhaseTimings.Capture(TestContext.Current).Should().BeSameAs(outer);
            var innerFailure = await Record.ExceptionAsync(() => inner.RunAsync<bool>(async () =>
            {
                await Task.Yield();
                NativeFixturePhaseTimings.Capture(TestContext.Current).Should().BeSameAs(inner);
                throw original;
            }));
            innerFailure.Should().BeSameAs(original);
            NativeFixturePhaseTimings.Capture(TestContext.Current).Should().BeSameAs(outer);
            return true;
        });
        NativeFixturePhaseTimings.Capture(TestContext.Current).Should().NotBeSameAs(outer).And.NotBeSameAs(inner);
    }

    [Fact]
    public async Task PublicationFailureIsNotReinterpretedAsSuccessfulOperation()
    {
        var failure = new IOException("Synthetic report delivery failure");
        var timing = new NativeFixturePhaseTimings("original-test-id", new PhaseClock(), _ => throw failure);
        var called = false;
        var thrown = await Record.ExceptionAsync(() => timing.RunAsync(() =>
        {
            called = true;
            return Task.FromResult(true);
        }));
        thrown.Should().BeSameAs(failure);
        called.Should().BeTrue("report delivery failure is rethrown after the owned operation, not instead of cleanup");
    }

    [Fact]
    public async Task TerminalPublicationFailureCannotSkipOwnedJoinOrLeaveAmbientOwner()
    {
        var failure = new IOException("Synthetic terminal report failure");
        var reports = 0;
        var timing = new NativeFixturePhaseTimings("original-test-id", new PhaseClock(), _ =>
        {
            if (++reports == 2) { throw failure; }
        });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var joined = false;
        var operation = timing.RunAsync(async () =>
        {
            entered.SetResult();
            try { await release.Task; }
            finally { joined = true; }
            return true;
        });
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        reports.Should().Be(1);
        release.SetResult();
        var thrown = await Record.ExceptionAsync(() => operation);
        thrown.Should().BeSameAs(failure);
        joined.Should().BeTrue();
        NativeFixturePhaseTimings.Capture(TestContext.Current).Should().NotBeSameAs(timing);
    }

    [Fact]
    public async Task ReportFailureDuringActualConstructionCleansItsUniqueStorageBeforeRethrow()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            var before = Directory.GetDirectories(parent, "kora-native-ux-*");
            var failure = new IOException("Synthetic construction report failure");
            var timing = new NativeFixturePhaseTimings("construction-execution", new PhaseClock(), _ => throw failure);
            var thrown = await Record.ExceptionAsync(() => timing.RunAsync(() =>
            {
                using var fixture = new NativeUxFixtureSession(parent);
                return Task.FromResult(true);
            }));
            thrown.Should().BeSameAs(failure);
            Directory.GetDirectories(parent, "kora-native-ux-*").Should().BeEquivalentTo(before);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualDispatchedFailureOrCancellationRetainsTerminalOutcomeAndRestoresOwner(bool cancelled)
    {
        var context = TestContext.Current;
        var original = context.Test!.UniqueID;
        const string forbidden = "PRIVATE_TRANSCRIPT_FILENAME_AUDIT";
        Exception failure = cancelled ? new OperationCanceledException(forbidden) : new IOException(forbidden);
        var thrown = await Record.ExceptionAsync(() => HeadlessSession.RunAsync((Action)(() => throw failure)));
        thrown.Should().BeSameAs(failure);
        using (var terminal = JsonDocument.Parse(context.Attachments![NativeFixturePhaseTimings.AttachmentName].AsString()))
        {
            terminal.RootElement.GetProperty("TestUniqueId").GetString().Should().Be(original);
            terminal.RootElement.GetProperty("PhaseStates")[0].GetProperty("Outcome").GetString()
                .Should().Be(cancelled ? "cancelled" : "failed");
            terminal.RootElement.GetRawText().Should().NotContain(forbidden);
        }
        await HeadlessSession.RunAsync((Action)(() => { }));
        using var restored = JsonDocument.Parse(context.Attachments![NativeFixturePhaseTimings.AttachmentName].AsString());
        restored.RootElement.GetProperty("PhaseStates").GetArrayLength().Should().Be(2);
        restored.RootElement.GetProperty("PhaseStates")[1].GetProperty("Outcome").GetString().Should().Be("completed");
        restored.RootElement.GetProperty("TestUniqueId").GetString().Should().Be(original);
    }

    [Fact]
    public async Task ConcurrentOwnersKeepOriginalIdentityAcrossAwaitAndDoNotRetargetEachOther()
    {
        var firstReports = new List<string>();
        var secondReports = new List<string>();
        var first = new NativeFixturePhaseTimings("first-execution", new PhaseClock(), firstReports.Add);
        var second = new NativeFixturePhaseTimings("second-execution", new PhaseClock(), secondReports.Add);
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        static async Task<bool> ObserveOwner(NativeFixturePhaseTimings owner, TaskCompletionSource entered, Task other)
        {
            NativeFixturePhaseTimings.Capture(TestContext.Current).Should().BeSameAs(owner);
            entered.SetResult();
            await other.WaitAsync(TestContext.Current.CancellationToken);
            NativeFixturePhaseTimings.Capture(TestContext.Current).Should().BeSameAs(owner);
            return true;
        }
        await Task.WhenAll(first.RunAsync(() => ObserveOwner(first, firstEntered, secondEntered.Task)),
            second.RunAsync(() => ObserveOwner(second, secondEntered, firstEntered.Task)));
        firstReports.Should().OnlyContain(json => json.Contains("first-execution", StringComparison.Ordinal)
            && !json.Contains("second-execution", StringComparison.Ordinal));
        secondReports.Should().OnlyContain(json => json.Contains("second-execution", StringComparison.Ordinal)
            && !json.Contains("first-execution", StringComparison.Ordinal));
        NativeFixturePhaseTimings.Capture(TestContext.Current).Should().NotBeSameAs(first).And.NotBeSameAs(second);
    }

    [Fact]
    public async Task ActualConstructorFailureNeverCapturesInvalidPathOrInventsLaterPhases()
    {
        var context = TestContext.Current;
        await HeadlessSession.RunAsync((Action)(() =>
        {
            var create = () => new NativeUxFixtureSession("PRIVATE_INVALID_FILENAME");
            create.Should().Throw<ArgumentException>();
        }));
        using var report = JsonDocument.Parse(context.Attachments![NativeFixturePhaseTimings.AttachmentName].AsString());
        var states = report.RootElement.GetProperty("PhaseStates").EnumerateArray().ToArray();
        states.Select(state => state.GetProperty("Phase").GetString()).Should().Equal("native-ui", "owned-storage");
        states.Select(state => state.GetProperty("Outcome").GetString()).Should().Equal("completed", "failed");
        report.RootElement.GetRawText().Should().NotContain("PRIVATE_INVALID_FILENAME");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ActualNativeFixtureRetainsOriginalTheoryIdentityAllPhasesRenderingAndCleanup(int invocation)
    {
        var context = TestContext.Current;
        var original = context.Test!.UniqueID;
        var owner = NativeFixturePhaseTimings.Capture(context);
        TimeProvider? productClock = invocation == 2
            ? new PhaseClock { UtcNow = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero) }
            : null;
        string? root = null;
        await HeadlessSession.RunAsync(async () =>
        {
            NativeFixturePhaseTimings.Capture(TestContext.Current).Should().BeSameAs(owner);
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var fixture = new NativeUxFixtureSession(parent, timeProvider: productClock);
            fixture.Time.Should().BeSameAs(productClock ?? TimeProvider.System);
            root = fixture.LocalRoot;
            await fixture.InitializeAsync();
            fixture.Main.HasVoiceConsent.Should().BeFalse();
            var window = new Window { Width = 160 + invocation, Height = 120, Content = new TextBlock { Text = "Synthetic fixture" } };
            window.Show();
            try
            {
                using var frame = window.CaptureRenderedFrame();
                frame.Should().NotBeNull("the unchanged headless fixture must use real Skia rendering");
            }
            finally { window.Close(); }
        });
        Directory.Exists(root).Should().BeFalse();
        await HeadlessSession.RunAsync((Action)(() => NativeFixturePhaseTimings.Capture(TestContext.Current).Should().BeSameAs(owner)));
        (await HeadlessSession.RunAsync(() => invocation)).Should().Be(invocation);
        using var report = JsonDocument.Parse(context.Attachments![NativeFixturePhaseTimings.AttachmentName].AsString());
        report.RootElement.GetProperty("TestUniqueId").GetString().Should().Be(original);
        var states = report.RootElement.GetProperty("PhaseStates").EnumerateArray().ToArray();
        states.Select(state => state.GetProperty("Phase").GetString())
            .Should().Equal("native-ui", "owned-storage", "setup", "context", "disposal", "join", "evidence-flush", "native-ui", "native-ui");
        states.Should().OnlyContain(state => state.GetProperty("Outcome").GetString() == "completed");
        var steps = report.RootElement.GetProperty("Timing").GetProperty("StepTimings").EnumerateArray().ToArray();
        steps.Should().HaveCount(states.Length);
        steps.Should().OnlyContain(step => TimeSpan.Parse(step.GetProperty("Timing").GetProperty("Duration").GetString()!,
            System.Globalization.CultureInfo.InvariantCulture) >= TimeSpan.Zero);
        if (productClock is not null)
        {
            productClock.GetTimestamp().Should().Be(0);
            report.RootElement.GetProperty("Timing").GetProperty("GlobalTiming")
                .GetProperty("StartTime").GetDateTimeOffset().Year.Should().NotBe(2000);
        }
        report.RootElement.GetRawText().Should().NotContain(root!);
    }

    private sealed class PhaseClock : TimeProvider
    {
        private long timestamp;
        internal DateTimeOffset UtcNow { get; set; } = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        public override long TimestampFrequency => 1000;
        public override DateTimeOffset GetUtcNow() => UtcNow;
        public override long GetTimestamp() => Interlocked.Read(ref timestamp);
        internal void Advance(long milliseconds) => Interlocked.Add(ref timestamp, milliseconds);
    }
}
