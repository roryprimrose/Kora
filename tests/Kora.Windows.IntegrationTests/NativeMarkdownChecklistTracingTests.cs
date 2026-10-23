using System.Diagnostics;

using AwesomeAssertions;

using Kora.Core.Presentation;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection("Host tracing")]
public sealed class NativeMarkdownChecklistTracingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RenderRetainsParentageTruthfulProfileOutcomeAndNoContentTags(bool qualified)
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
        var renderer = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance, checklistsAvailable: qualified);
        renderer.Render("- [x] NEVER_TAG_CONTENT", format: NativeDetailFormat.MarkdownChecklistsV3)
            .IsFallback.Should().Be(!qualified);
        renderer.Render("- [ ] <b>NEVER_TAG_CONTENT</b>", format: NativeDetailFormat.MarkdownChecklistsV3)
            .IsFallback.Should().BeTrue();
        completed.Should().HaveCount(2);
        completed.Should().OnlyContain(activity => activity.ParentSpanId == parent.SpanId
            && activity.OperationName == "presentation.render");
        completed[0].Status.Should().Be(qualified ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        completed[1].Status.Should().Be(ActivityStatusCode.Error);
        completed.Should().OnlyContain(activity =>
            string.Equals(activity.GetTagItem("kora.presentation.profile") as string,
                NativeMarkdownChecklistProfile.Name, StringComparison.Ordinal));
        completed.SelectMany(activity => activity.Tags).Should()
            .NotContain(tag => tag.Value != null && tag.Value.Contains("NEVER_TAG_CONTENT", StringComparison.Ordinal));
        completed.SelectMany(activity => activity.Baggage).Should().BeEmpty();
        Activity.Current.Should().BeSameAs(parent);
    }
}
