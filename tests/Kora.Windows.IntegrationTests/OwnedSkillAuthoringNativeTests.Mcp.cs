using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using AwesomeAssertions;
using Kora.Core.Skills;
using Kora.NativeUxFixture;
using Kora.Windows.Skills;

namespace Kora.Windows.IntegrationTests;

public sealed partial class OwnedSkillAuthoringNativeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnedMcpNativeWriterRequiresSeparatePublicationAndReadsBackDisabledExactRevision(bool cancel)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            var store = new WindowsOwnedDeclarativeSkillStore(native);
            var editor = new OwnedSkillEditorWindow(_ => { }, static () => true);
            editor.IdInput.Text = "reference";
            editor.VersionInput.Text = "1.0.0";
            editor.NameInput.Text = "Reference";
            editor.DescriptionInput.Text = "Review one reference";
            editor.Guidance.Text = "  Original literal\r\n${input:ignored}  ";
            editor.Tool.SelectedIndex = 2;
            editor.McpServer.Text = new string('a', 64);
            editor.McpSourceRevision.Text = new string('b', 64);
            editor.McpTool.Text = "search";
            editor.McpSchemaDigest.Text = new string('c', 64);
            var document = editor.Document();
            editor.Show();
            var dialogs = 0;
            using var handler = Window.WindowOpenedEvent.AddClassHandler<Window>((dialog, _) =>
            {
                if (ReferenceEquals(dialog, editor)) { return; }
                dialogs++;
                var boxes = dialog.GetLogicalDescendants().OfType<TextBox>().ToArray();
                boxes[0].Text.Should().Be(document.ManifestText);
                boxes[1].Text.Should().Be(document.Guidance);
                var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();
                Avalonia.Threading.Dispatcher.UIThread.Post(() => buttons[cancel && dialogs == 2 ? ^1 : 0]
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            });
            try
            {
                (await editor.Review(document, store.Destination(document), native.Token)).Should().BeTrue();
                var confirmed = await editor.ConfirmPublication(document, store.Destination(document), native.Token);
                confirmed.Should().Be(!cancel);
                if (confirmed)
                {
                    var published = await store.Publish(document, static () => true, commit => Task.FromResult(commit()), native.Token);
                    var reopened = await store.Read(document.Id, document.Version, native.Token);
                    reopened!.IsSameRevision(published).Should().BeTrue();
                    reopened.Document.McpReference.Should().Be(document.McpReference);
                    reopened.Document.IsAvailableForInvocation.Should().BeFalse();
                    reopened.Document.ManifestBytes.ToArray().Should().Equal(document.ManifestBytes.ToArray());
                    reopened.Document.InstructionBytes.ToArray().Should().Equal(document.InstructionBytes.ToArray());
                }
                else { (await store.Read(document.Id, document.Version, native.Token)).Should().BeNull(); }
                dialogs.Should().Be(2);
            }
            finally { editor.Close(); }
        });
    }
}
