using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Artifacts;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class OwnedDefinitionEditorWindowTests
{
    [Theory]
    [InlineData(ArtifactKind.Instruction, false)]
    [InlineData(ArtifactKind.Prompt, false)]
    [InlineData(ArtifactKind.Instruction, true)]
    [InlineData(ArtifactKind.Prompt, true)]
    public async Task SourceSizeIsPassiveExactAccessibleAndDoesNotChangeReadOnlyAuthority(ArtifactKind kind, bool readOnly)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var actions = new List<string>();
            var window = new OwnedInstructionEditorWindow(actions.Add);
            window.Show();
            try
            {
                window.SelectKind(kind);
                window.Source.IsReadOnly = readOnly;
                AutomationProperties.GetAutomationId(window.SourceSizeStatus).Should().Be("OwnedSourceBufferSize");
                var peer = ControlAutomationPeer.CreatePeerForElement(window.SourceSizeStatus)!;
                peer.GetName().Should().Be(window.SourceSizeStatus.Text);
                AutomationProperties.GetLiveSetting(window.SourceSizeStatus).Should().Be(AutomationLiveSetting.Off);
                AutomationProperties.GetHelpText(window.SourceSizeStatus).Should().Contain("No automatic announcements");
                window.SourceSizeStatus.Text.Should().Contain("0 UTF-16 code units; 0 UTF-8 bytes; 0 Unicode scalars");
                window.ReportValidation("Existing full validation observation");
                const string exact = "\ufeff---\r\nnot: schema\n---\re\u0301\ud83d\ude00";
                window.Source.Text = exact;
                var size = ArtifactFileDocument.MeasureSourceBuffer(exact);
                window.SourceSizeStatus.Text.Should().Contain($"{size.Utf16CodeUnits} UTF-16 code units")
                    .And.Contain($"{size.Utf8Bytes} UTF-8 bytes").And.Contain($"{size.UnicodeScalars} Unicode scalars")
                    .And.Contain($"Source-file byte ceiling: {ArtifactDefinition.MaximumContentLength}")
                    .And.Contain("not a separately retained file BOM").And.Contain("Validate remains separate");
                window.Source.Text.Should().Be(exact);
                peer.GetName().Should().Be(window.SourceSizeStatus.Text);
                window.Source.IsReadOnly.Should().Be(readOnly);
                window.GetVisualDescendants().OfType<TextBlock>().Should()
                    .Contain(block => string.Equals(block.Text, "Existing full validation observation", StringComparison.Ordinal));
                window.ReviewUse.IsEnabled.Should().BeFalse();
                window.Source.MaxLength.Should().Be(0);
                actions.Should().BeEmpty("measurement must not dispatch Validate, discovery, save, use or another action");

                var changes = 0;
                window.SourceSizeStatus.PropertyChanged += (_, change) =>
                {
                    if (change.Property == TextBlock.TextProperty) { changes++; }
                };
                window.Source.Text = exact;
                changes.Should().Be(0, "identical assignments must not churn notifications");
                window.Source.Text = new string('\ud800', 1);
                changes.Should().Be(1);
                window.SourceSizeStatus.Text.Should().Contain("1 UTF-16 code units")
                    .And.Contain("UTF-8 bytes unavailable; Unicode scalars unavailable")
                    .And.Contain("ill-formed Unicode (unpaired UTF-16 surrogate)");
                peer.GetName().Should().Be(window.SourceSizeStatus.Text);
                window.Source.Text.Should().Be(new string('\ud800', 1));
                window.Source.Text = new string('\udfff', 1);
                changes.Should().Be(1, "equivalent malformed-size states have identical passive status");

                window.Source.Text = new string('x', ArtifactDefinition.MaximumContentLength);
                window.SourceSizeStatus.Text.Should().Contain($"{ArtifactDefinition.MaximumContentLength} UTF-8 bytes")
                    .And.NotContain("Over source-file");
                window.Source.Text += "x";
                window.SourceSizeStatus.Text.Should().Contain("Over source-file byte ceiling; draft remains editable");
                window.Source.Text.Should().HaveLength(ArtifactDefinition.MaximumContentLength + 1);
                window.Source.IsReadOnly.Should().Be(readOnly);
                window.Source.Text = null;
                window.SourceSizeStatus.Text.Should().Contain("0 UTF-16 code units; 0 UTF-8 bytes; 0 Unicode scalars");
                actions.Should().BeEmpty();
            }
            finally { window.Close(); }
            window.SourceSizeStatus.Text.Should().BeEmpty();
            window.Source.Text = "retired";
            window.SourceSizeStatus.Text.Should().BeEmpty("closed windows must unsubscribe from source changes");
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task SourceSizeFollowsReentrantReplacementAndPrivateRetirementWithoutFeedback()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var calls = 0;
            var window = new OwnedInstructionEditorWindow(_ => calls++);
            window.Show();
            try
            {
                window.Source.PropertyChanged += (_, change) =>
                {
                    if (change.Property == TextBox.TextProperty && string.Equals(window.Source.Text, "first", StringComparison.Ordinal))
                    { window.Source.Text = "e\u0301"; }
                };
                window.Source.Text = "first";
                window.Source.Text.Should().Be("e\u0301");
                window.SourceSizeStatus.Text.Should().Contain("2 UTF-16 code units; 3 UTF-8 bytes; 2 Unicode scalars");
                window.SelectKind(ArtifactKind.Prompt);
                window.SourceSizeStatus.Text.Should().Contain("2 UTF-16 code units; 3 UTF-8 bytes; 2 Unicode scalars");
                window.Source.Text = "loaded\r\n";
                window.SourceSizeStatus.Text.Should().Contain("8 UTF-16 code units; 8 UTF-8 bytes; 8 Unicode scalars");
                calls.Should().Be(0);
                window.ClearPrivateText();
                window.SourceSizeStatus.Text.Should().BeEmpty();
                window.Source.Text.Should().BeEmpty();
                var clearedCalls = calls;
                window.Source.Text = "new";
                window.SourceSizeStatus.Text.Should().Contain("3 UTF-16 code units; 3 UTF-8 bytes; 3 Unicode scalars");
                calls.Should().Be(clearedCalls);
            }
            finally { window.Close(); }
            window.SourceSizeStatus.Text.Should().BeEmpty();
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction, false)]
    [InlineData(ArtifactKind.Prompt, false)]
    [InlineData(ArtifactKind.Instruction, true)]
    [InlineData(ArtifactKind.Prompt, true)]
    public async Task SourceSizeFollowsActualLoadCreateControlAndDisposalWithoutStoreAccess(ArtifactKind kind, bool create)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            OwnedInstructionEditorWindow editor;
            await using (var f = await DraftValidationFixture.Create(kind, create))
            {
                editor = f.Editor!;
                var loaded = editor.Source.Text!;
                editor.SourceSizeStatus.Text.Should().Contain($"{ArtifactFileDocument.MeasureSourceBuffer(loaded).Utf8Bytes} UTF-8 bytes");
                f.Store.DenyAccess = true;
                var catalogue = f.Catalogue.Candidates;
                var rows = editor.Files.ItemsSource;
                var selected = editor.Files.SelectedItem;
                var sourceBytes = f.Source?.Document.CopyBytes();
                editor.Source.Text = "not a definition\r\n\u00e9\ud83d\ude00" + '\ud800';
                editor.SourceSizeStatus.Text.Should().Contain("UTF-8 bytes unavailable; Unicode scalars unavailable");
                var draft = editor.Source.Text;
                var status = editor.SourceSizeStatus.Text;
                editor.NameInput.Text = "other";
                editor.CopyNameInput.Text = "separate";
                f.Session.Main.CommandText = "unrelated control input";
                editor.Source.Text.Should().Be(draft);
                editor.SourceSizeStatus.Text.Should().Be(status);
                editor.Files.ItemsSource.Should().BeSameAs(rows);
                editor.Files.SelectedItem.Should().BeSameAs(selected);
                f.Catalogue.Candidates.Should().BeSameAs(catalogue);
                f.Source?.Document.CopyBytes().Should().Equal(sourceBytes!);
                f.Store.Reads.Should().Be(0);
                f.Store.Lists.Should().Be(0);
                f.Store.Writes.Should().Be(0);
                editor.Source.Text = loaded;
                editor.SourceSizeStatus.Text.Should().Contain($"{ArtifactFileDocument.MeasureSourceBuffer(loaded).Utf8Bytes} UTF-8 bytes");
            }
            editor.Source.Text.Should().BeEmpty();
            editor.SourceSizeStatus.Text.Should().BeEmpty();
            editor.IsVisible.Should().BeFalse();
            editor.Source.Text = "late retired input";
            editor.SourceSizeStatus.Text.Should().BeEmpty();
        });
    }
}
