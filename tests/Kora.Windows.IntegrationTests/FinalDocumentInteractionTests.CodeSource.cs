using Avalonia.Controls;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.CodeHighlighting;
using Kora.Core.Context;

namespace Kora.Windows.IntegrationTests;

public sealed partial class FinalDocumentInteractionTests
{
    [Theory]
    [InlineData(false, "> ```cs\n> alpha\n> beta\n> ```\n")]
    [InlineData(true, "> ```cs\r\n> alpha\r\n> beta\r\n> ```\r\n")]
    [InlineData(true, "- ```cs\n  alpha\n  beta\n  ```\n")]
    [InlineData(false, "    alpha\n    beta\n")]
    public Task ContainerCodeFallbackReadsTheCompleteExactFinalAnswerAndRetiresWithoutCopy(bool retained, string code) =>
        HeadlessSession.RunAsync(() =>
        {
            var text = "# Original answer\n\n" + code + "\n[untrusted site](" + Address + ")\n";
            var admitted = true;
            var result = Render(text);
            var state = State(text, () => admitted, retained, result);
            var content = state.Content!;
            var view = new DetailContentView(state, result,
                (_, _, _, _) => throw new InvalidOperationException("Fallback reading grants no clipboard authority."));
            var window = new Window { Content = view, Width = 900, Height = 1000 };
            window.Show();
            try
            {
                var reader = view.FindControl<TextBox>("ContentReader")!;
                reader.IsVisible.Should().BeTrue();
                reader.Text.Should().Be(text);
                state.RenderState.Should().Be(Kora.Application.Presentation.DetailRenderState.SourceFallback);
                state.Status.Should().Contain("code source").And.Contain("fallback");
                state.Content.Should().BeSameAs(content);
                state.Content!.Digest.Should().Be(content.Digest);
                view.GetVisualDescendants().Should().NotContain(control => control is NativeCodeBlock);
                view.GetVisualDescendants().OfType<Button>().Should().NotContain(button => button.Tag is NativeExternalDocumentLink);
                window.OwnedWindows.Should().BeEmpty();

                state.Find("beta", SnapshotFindDirection.First).Should().BeTrue();
                state.MatchStart.Should().Be(text.IndexOf("beta", StringComparison.Ordinal));
                reader.SelectAll();
                reader.SelectedText.Should().Be(text);
                state.TryGetCopySource(true, false, out var unconfirmed).Should().BeFalse();
                unconfirmed.Should().BeNull();
                state.TryGetCopySource(true, true, out var confirmed).Should().Be(!retained);
                confirmed.Should().Be(retained ? null : text);
                view.FindControl<CheckBox>("SourceToggle")!.IsChecked = true;
                reader.Text.Should().Be(text);

                admitted = false;
                state.Content.Should().BeNull();
                reader.Text.Should().BeEmpty();
                reader.SelectedText.Should().BeEmpty();
                state.MatchStart.Should().Be(-1);
                view.FindControl<StackPanel>("RenderedDocument")!.Children.Should().BeEmpty();
                window.OwnedWindows.Should().BeEmpty();
            }
            finally { view.ClearPresentation(); window.Close(); }
        });
}
