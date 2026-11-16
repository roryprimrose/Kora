using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Configuration;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class AssistantNameDisplayTests
{
    [Theory]
    [InlineData("Sessions", "Nova - Sessions")]
    [InlineData("Nova", "Nova")]
    [InlineData("nova settings - 1.0", "nova settings - 1.0")]
    [InlineData("Novak notes", "Nova - Novak notes")]
    [InlineData("  ", "Nova")]
    [InlineData(null, "Nova")]
    public void TitleComposerAddsAssistantNameOnlyWhenTheTitleDoesNotAlreadyStartWithIt(string? source, string expected) =>
        AssistantWindowTitles.Compose(source, "Nova").Should().Be(expected);

    [Fact]
    public Task WindowTitlesFollowRenameKeepBoundSourcesAndStopAfterDispose() =>
        HeadlessSession.RunAsync(() =>
        {
            var display = new AssistantNameDisplay();
            var literal = new Window { Title = "Sessions" };
            var bound = new Window();
            bound.Bind(Window.TitleProperty, new Binding(nameof(AssistantNameDisplay.Name)) { Source = display });
            var titles = AssistantWindowTitles.Install(() => [literal, bound], display);
            try
            {
                literal.Title.Should().Be("Kora - Sessions");
                bound.Title.Should().Be("Kora");
                literal.Title = "Settings";
                literal.Title.Should().Be("Kora - Settings");

                display.Update("Nova");

                literal.Title.Should().Be("Nova - Settings", "the retained source is recomposed with the new name");
                bound.Title.Should().Be("Nova", "the original binding still drives a title that is already the name");
            }
            finally { titles.Dispose(); }
            literal.Title = "Plain";
            literal.Title.Should().Be("Plain");
        });

    [Fact]
    public Task XamlAssistantReferencesFollowTheConfiguredName() =>
        HeadlessSession.RunAsync(() =>
        {
            var window = new Window { Content = new CaptionDisplaySelector() };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Text(window, "restarts").Should().Be("Caption screen — until Kora restarts");

                AssistantNameDisplay.Current.Update("Nova");
                Dispatcher.UIThread.RunJobs();

                Text(window, "restarts").Should().Be("Caption screen — until Nova restarts");
            }
            finally
            {
                AssistantNameDisplay.Current.Update(AssistantNameRules.DefaultName);
                window.Close();
            }
        });

    private static string Text(Window window, string fragment) =>
        window.GetVisualDescendants().OfType<TextBlock>()
            .Single(block => block.Text?.Contains(fragment, StringComparison.Ordinal) == true).Text!;
}
