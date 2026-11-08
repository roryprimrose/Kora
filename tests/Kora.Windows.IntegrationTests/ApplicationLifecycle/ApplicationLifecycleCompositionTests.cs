using AwesomeAssertions;

using Kora.Core.Presentation;

using Microsoft.Extensions.DependencyInjection;

namespace Kora.Windows.IntegrationTests;

public sealed class ApplicationLifecycleCompositionTests
{
    [Fact]
    public void LifecycleRendererUsesRegisteredLoggerWithoutRequiringRendererRegistration()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();

        var renderer = App.CreateApplicationLifecycleRenderer(services);
        const string source = "# Lifecycle review\n\nAccepted work must stop before exit.";
        var result = renderer.Render(source);

        result.IsFallback.Should().BeFalse();
        result.Source.Should().Be(source);
        result.SemanticText.Should().Contain("Lifecycle review")
            .And.Contain("Accepted work must stop before exit.");
    }

    [Fact]
    public void LifecycleRendererKeepsUnqualifiedProfilesUnavailable()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();

        var renderer = App.CreateApplicationLifecycleRenderer(services);
        foreach (var (format, source) in new[]
        {
            (NativeDetailFormat.MarkdownChecklistsV3, "- [ ] pending work"),
            (NativeDetailFormat.MarkdownStrikethroughV4, "~~retired work~~"),
        })
        {
            var result = renderer.Render(source, format: format);

            result.IsFallback.Should().BeTrue();
            result.Source.Should().Be(source);
            result.SemanticText.Should().Be(source);
            result.Status.Should().Contain("unavailable pending profile qualification; exact source fallback.");
        }
    }
}
