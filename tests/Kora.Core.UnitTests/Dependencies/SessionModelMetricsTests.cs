using System.Collections.Immutable;
using System.Text;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class SessionModelMetricsTests
{
    private static ModelUsageSummary Model => new(ModelProviderIdentity.Ollama, "local-model", 2, 2, 2, 120, 40, 80, 200, TimeSpan.FromSeconds(1));
    private static SessionModelMetrics Snapshot => new(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1),
        2, 2, 2, 120, 40, [Model]);

    [Fact]
    public void ReportsMultipleModelsWithoutCombiningTheirContextWindows()
    {
        var second = Model with { Provider = ModelProviderIdentity.Claude, Model = "hosted-model", LatestContextTokens = 100, ContextCapacityTokens = 1000 };
        var snapshot = Snapshot with { Executions = 4, InputReportedExecutions = 4, OutputReportedExecutions = 4,
            InputTokens = 240, OutputTokens = 80, Models = [Model, second] };
        snapshot.Validate();
        (snapshot with { Retained = true }).Format().Should().StartWith("Recorded session model usage");
        snapshot.FormatSpeech().Should().Contain("240").And.Contain("2 models");
        snapshot.Format().Should().Contain("240").And.Contain("Ollama / local-model").And.Contain("Claude / hosted-model")
            .And.Contain("40.0%").And.Contain("10.0%").And.Contain("not reported; no price estimate")
            .And.NotContain("50.0%");
        Model.ContextPercent.Should().Be(40);
        Model.HasContextPercent.Should().BeTrue();
        second.ContextPercent.Should().Be(10);
        var json = Encoding.UTF8.GetString(CapabilityReplyWire.Serialize(new(CapabilityOutcome.Succeeded, "observed", SessionMetrics: snapshot)));
        json.Should().NotContain("contextPercent").And.NotContain("inputLabel").And.NotContain("label");
    }

    [Theory]
    [InlineData(0, 0, "not reported")]
    [InlineData(1, 0, "partial; 1/2")]
    [InlineData(2, 2, "(reported)")]
    public void DistinguishesUnreportedPartialAndReportedZero(long inputReports, long outputReports, string expected)
    {
        var row = Model with { InputReportedExecutions = inputReports, OutputReportedExecutions = outputReports,
            InputTokens = 0, OutputTokens = 0, LatestContextTokens = null, ContextCapacityTokens = null };
        var snapshot = Snapshot with { InputReportedExecutions = inputReports, OutputReportedExecutions = outputReports,
            InputTokens = 0, OutputTokens = 0, Models = [row] };
        snapshot.Validate();
        snapshot.Format().Should().Contain(expected).And.Contain("Latest request context: not reported");
        snapshot.FormatSpeech().Should().Contain(inputReports == 0 ? "input not reported" : "input 0 tokens");
        row.ContextPercent.Should().Be(0);
        row.HasContextPercent.Should().BeFalse();
    }

    [Fact]
    public void ReportsMissingCapacityNoExecutionsAndExplicitToolOmissions()
    {
        var row = Model with { ContextCapacityTokens = null };
        row.ContextLabel.Should().Contain("80 tokens; capacity not reported");
        row.ContextPercent.Should().Be(0);
        row.HasContextPercent.Should().BeFalse();
        (Snapshot with { Models = [row], OmittedModels = 2 }).Format().Should().Contain("2 model rows omitted");
        var empty = Snapshot with { Executions = 0, InputReportedExecutions = 0, OutputReportedExecutions = 0,
            InputTokens = 0, OutputTokens = 0, Models = [] };
        empty.Format().Should().Contain("No finished model execution");
        empty.FormatSpeech().Should().Contain("not reported");
    }

    [Theory]
    [InlineData("start-offset")]
    [InlineData("end-offset")]
    [InlineData("clock")]
    [InlineData("executions")]
    [InlineData("input-report-negative")]
    [InlineData("input-report-overflow")]
    [InlineData("output-report-negative")]
    [InlineData("output-report-overflow")]
    [InlineData("input")]
    [InlineData("output")]
    [InlineData("models")]
    [InlineData("omitted")]
    [InlineData("model-executions")]
    [InlineData("model-input")]
    [InlineData("model-output")]
    public void RejectsInvalidSessionObservations(string fault)
    {
        var snapshot = fault switch
        {
            "start-offset" => Snapshot with { StartedAt = DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(1)) },
            "end-offset" => Snapshot with { ObservedAt = DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(1)) },
            "clock" => Snapshot with { ObservedAt = DateTimeOffset.UnixEpoch.AddSeconds(-1) },
            "executions" => Snapshot with { Executions = -1 },
            "input-report-negative" => Snapshot with { InputReportedExecutions = -1 },
            "input-report-overflow" => Snapshot with { InputReportedExecutions = 3 },
            "output-report-negative" => Snapshot with { OutputReportedExecutions = -1 },
            "output-report-overflow" => Snapshot with { OutputReportedExecutions = 3 },
            "input" => Snapshot with { InputTokens = -1 },
            "output" => Snapshot with { OutputTokens = -1 },
            "models" => Snapshot with { Models = default(ImmutableArray<ModelUsageSummary>) },
            "omitted" => Snapshot with { OmittedModels = -1 },
            "model-executions" => Snapshot with { Models = [Model with { Executions = 3 }] },
            "model-input" => Snapshot with { Models = [Model with { InputTokens = 121 }] },
            _ => Snapshot with { Models = [Model with { OutputTokens = 41 }] },
        };
        snapshot.Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("name-null")]
    [InlineData("name-empty")]
    [InlineData("name-long")]
    [InlineData("name-control")]
    [InlineData("executions")]
    [InlineData("input-report-negative")]
    [InlineData("input-report-overflow")]
    [InlineData("output-report-negative")]
    [InlineData("output-report-overflow")]
    [InlineData("input")]
    [InlineData("output")]
    [InlineData("duration")]
    public void RejectsInvalidModelObservations(string fault)
    {
        var model = fault switch
        {
            "provider" => Model with { Provider = (ModelProviderIdentity)999 },
            "name-null" => Model with { Model = null! },
            "name-empty" => Model with { Model = "" },
            "name-long" => Model with { Model = new string('x', 257) },
            "name-control" => Model with { Model = "unsafe\n" },
            "executions" => Model with { Executions = 0 },
            "input-report-negative" => Model with { InputReportedExecutions = -1 },
            "input-report-overflow" => Model with { InputReportedExecutions = 3 },
            "output-report-negative" => Model with { OutputReportedExecutions = -1 },
            "output-report-overflow" => Model with { OutputReportedExecutions = 3 },
            "input" => Model with { InputTokens = -1 },
            "output" => Model with { OutputTokens = -1 },
            _ => Model with { TotalElapsed = TimeSpan.FromSeconds(-1) },
        };
        model.Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
    }
}
