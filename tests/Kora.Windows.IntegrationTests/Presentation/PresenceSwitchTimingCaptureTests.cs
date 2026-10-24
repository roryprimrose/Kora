using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
using AwesomeAssertions;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests.Presentation;

[Collection(nameof(ProcessGlobalEvidenceTestGroup))]
public sealed class PresenceSwitchTimingCaptureTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Capture_is_disabled_by_default(string? setting)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        PresenceSwitchTimingCapture.Create(setting, output).Should().BeNull();
        output.ToString().Should().BeEmpty();
    }

    [Theory]
    [InlineData("true")]
    [InlineData("0")]
    [InlineData(" 1")]
    public void Unknown_opt_in_values_fail_explicitly(string setting)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var create = () => PresenceSwitchTimingCapture.Create(setting, output);
        create.Should().Throw<InvalidDataException>().WithMessage("*absent or exactly 1*");
        output.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task Capture_contains_only_allowlisted_phase_names_and_monotonic_timestamps_and_joins_output()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var capture = PresenceSwitchTimingCapture.Create("1", output)!;
        var before = Stopwatch.GetTimestamp();
        PresenceSwitchTiming.Mark(PresenceSwitchPhase.Selection);
        await Task.Run(() => PresenceSwitchTiming.Mark(PresenceSwitchPhase.RequestedAuditStarted),
            TestContext.Current.CancellationToken);
        PresenceSwitchTiming.Mark(PresenceSwitchPhase.FadeOutStarted);
        var after = Stopwatch.GetTimestamp();
        await capture.DisposeAsync();
        var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(4);
        lines[0].Should().Be("presence-timing-v1\tfrequency\t" + Stopwatch.Frequency.ToString(CultureInfo.InvariantCulture));
        var stamps = lines.Skip(1).Select(line => line.Split('\t')).ToArray();
        stamps.Should().OnlyContain(fields => fields.Length == 3 && string.Equals(fields[0], "presence-timing-v1", StringComparison.Ordinal));
        stamps.Select(fields => fields[1]).Should().Equal("Selection", "RequestedAuditStarted", "FadeOutStarted");
        var timestamps = stamps.Select(fields => long.Parse(fields[2], CultureInfo.InvariantCulture)).ToArray();
        timestamps.Should().BeInAscendingOrder().And.OnlyContain(tick => tick >= before && tick <= after);
        var finished = output.ToString();
        PresenceSwitchTiming.Mark(PresenceSwitchPhase.RestoreReturned);
        await capture.DisposeAsync();
        output.ToString().Should().Be(finished);
    }

    [Fact]
    public async Task Same_named_external_metrics_cannot_supply_text_or_other_values_to_the_capture()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        await using (var capture = PresenceSwitchTimingCapture.Create("1", output)!)
        {
            using var spoof = new Meter("Kora.PresenceTiming", "1.0.0");
            spoof.CreateHistogram<long>("kora.presence.renderer.phase.timestamp", "ticks")
                .Record(123, new KeyValuePair<string, object?>("phase", "private-user-text"));
        }
        output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Should().ContainSingle();
        output.ToString().Should().NotContain("private-user-text").And.NotContain("123");
    }

    [Fact]
    public async Task Invalid_measurement_on_the_real_instrument_fails_without_rendering_its_untrusted_value()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var capture = PresenceSwitchTimingCapture.Create("1", output)!;
        var instrument = PresenceSwitchTiming.Instrument.Should().BeOfType<Histogram<long>>().Subject;
        instrument.Record(123, new KeyValuePair<string, object?>("phase", "private-user-text"));
        var dispose = async () => await capture.DisposeAsync();
        (await dispose.Should().ThrowAsync<IOException>()).WithInnerException<IOException>();
        output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Should().ContainSingle();
        output.ToString().Should().NotContain("private-user-text");
    }

    [Fact]
    public async Task Slow_output_never_blocks_emission_and_capacity_failure_is_explicit_after_join()
    {
        using var output = new GatedOutput();
        var capture = PresenceSwitchTimingCapture.Create("1", output)!;
        try
        {
            await output.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var emission = Task.Run(() =>
            {
                for (var index = 0; index <= PresenceSwitchTimingCapture.Capacity; index++)
                {
                    PresenceSwitchTiming.Mark(PresenceSwitchPhase.Selection);
                }
            }, TestContext.Current.CancellationToken);
            await emission.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally { output.Release.TrySetResult(); }
        var dispose = async () => await capture.DisposeAsync();
        (await dispose.Should().ThrowAsync<IOException>()).WithInnerException<IOException>();
        output.Lines.Should().Be(PresenceSwitchTimingCapture.Capacity + 1);
    }

    [Fact]
    public async Task Output_failure_is_not_reported_as_successful_capture()
    {
        using var output = new FailedOutput();
        var capture = PresenceSwitchTimingCapture.Create("1", output)!;
        PresenceSwitchTiming.Mark(PresenceSwitchPhase.Selection);
        var dispose = async () => await capture.DisposeAsync();
        await dispose.Should().ThrowAsync<IOException>().WithMessage("Synthetic output failure.");
    }

    [Fact]
    public async Task Output_worker_does_not_inherit_the_admitting_host_context()
    {
        using var output = new ContextCheckingOutput();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var host = HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Presentation);
        await using (var capture = PresenceSwitchTimingCapture.Create("1", output)!)
        {
            PresenceSwitchTiming.Mark(PresenceSwitchPhase.Selection);
        }
        output.Lines.Should().Be(2);
        output.HadHostContext.Should().BeFalse();
    }

    private sealed class GatedOutput : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Lines { get; private set; }
        public override async Task WriteLineAsync(string? value)
        {
            Started.TrySetResult();
            await Release.Task;
            Lines++;
        }
    }

    private sealed class FailedOutput : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override Task WriteLineAsync(string? value) => Task.FromException(new IOException("Synthetic output failure."));
    }

    private sealed class ContextCheckingOutput : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public int Lines { get; private set; }
        public bool HadHostContext { get; private set; }
        public override Task WriteLineAsync(string? value)
        {
            Lines++;
            HadHostContext |= HostActivity.Current is not null;
            return Task.CompletedTask;
        }
    }
}
