using System.Diagnostics;

using AwesomeAssertions;

using Kora.Core.Presentation;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection("Host tracing")]
public sealed class NativeMarkdownTableTracingTests
{
    [Fact]
    public void RenderActivitiesRetainParentageTruthfulOutcomeAndNoSourceTags()
    {
        var completed = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "Kora.Desktop", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = completed.Add,
        };
        ActivitySource.AddActivityListener(listener);
        using var parent = new Activity("test-parent").Start();
        var renderer = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance, pipeTablesAvailable: true);
        renderer.Render("| Name | State |\n| --- | --- |\n| Alpha | Ready |",
            format: NativeDetailFormat.PipeTablesV2).IsFallback.Should().BeFalse();
        renderer.Render("| Secret | B |\n| --- | --- |\n| <b>NEVER_TAG</b> | value |",
            format: NativeDetailFormat.PipeTablesV2).IsFallback.Should().BeTrue();
        completed.Should().HaveCount(2);
        completed.Should().OnlyContain(activity => activity.ParentSpanId == parent.SpanId
            && activity.OperationName == "presentation.render");
        completed[0].Status.Should().Be(ActivityStatusCode.Ok);
        completed[1].Status.Should().Be(ActivityStatusCode.Error);
        completed.Should().OnlyContain(activity =>
            string.Equals(activity.GetTagItem("kora.presentation.profile") as string,
                NativeMarkdownTableProfile.Name, StringComparison.Ordinal));
        completed.SelectMany(activity => activity.Tags).Should()
            .NotContain(tag => tag.Value != null && tag.Value.Contains("NEVER_TAG", StringComparison.Ordinal));
    }
}
