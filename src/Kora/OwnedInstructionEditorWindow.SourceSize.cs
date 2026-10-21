using System.Globalization;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

using Kora.Core.Artifacts;

namespace Kora;

internal sealed partial class OwnedInstructionEditorWindow
{
    internal readonly TextBlock SourceSizeStatus = new() { TextWrapping = TextWrapping.Wrap };

    private void BindSourceSize()
    {
        AutomationProperties.SetAutomationId(SourceSizeStatus, "OwnedSourceBufferSize");
        AutomationProperties.SetHelpText(SourceSizeStatus,
            "Passive counts of the exact displayed source, including metadata and line endings. Not validation or save/use approval. No automatic announcements.");
        Source.PropertyChanged += OnSourceSizeChanged;
        Closed += (_, _) =>
        {
            Source.PropertyChanged -= OnSourceSizeChanged;
            SourceSizeStatus.Text = string.Empty;
        };
        UpdateSourceSize();
    }

    private void OnSourceSizeChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == TextBox.TextProperty) { UpdateSourceSize(); }
    }

    private void UpdateSourceSize()
    {
        var size = ArtifactFileDocument.MeasureSourceBuffer(Source.Text ?? string.Empty);
        var counts = size.Utf8Bytes is { } bytes
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes} UTF-8 bytes; {size.UnicodeScalars} Unicode scalars; well-formed Unicode.")
            : "UTF-8 bytes unavailable; Unicode scalars unavailable: ill-formed Unicode (unpaired UTF-16 surrogate).";
        var overLimit = size.Utf8Bytes > ArtifactDefinition.MaximumContentLength
            ? "Over source-file byte ceiling; draft remains editable. " : string.Empty;
        SourceSizeStatus.Text = string.Create(CultureInfo.InvariantCulture,
            $"Current displayed source buffer: {size.Utf16CodeUnits} UTF-16 code units; {counts} ")
            + string.Create(CultureInfo.InvariantCulture,
                $"Source-file byte ceiling: {ArtifactDefinition.MaximumContentLength} including any separately retained file BOM. ")
            + overLimit + "Counts include displayed metadata, line endings and any displayed BOM, not a separately retained file BOM, "
            + "persisted bytes, expanded prompt or resource package. No UTF-16/scalar source limit is defined here. "
            + "Not syntax/schema validation or save/use approval; Validate remains separate.";
    }
}
