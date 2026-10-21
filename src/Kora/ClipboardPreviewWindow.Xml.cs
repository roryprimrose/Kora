using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Context;

namespace Kora;

internal sealed partial class ClipboardPreviewWindow
{
    private NamedTextBlock? xmlStatus;
    private ClipboardSnapshot? xmlSource;
    private Func<bool>? xmlCurrent;
    private Func<ClipboardSnapshot, Func<bool>, CancellationToken, ClipboardXmlValidationResult>? xmlValidate;
    private long xmlGeneration;
    private long xmlOperation;

    private void AddXmlValidation(StackPanel chrome, ClipboardSnapshot snapshot, Func<bool> isCurrent,
        Func<ClipboardSnapshot, Func<bool>, CancellationToken, ClipboardXmlValidationResult>? validateXml)
    {
        xmlSource = snapshot;
        xmlCurrent = isCurrent;
        xmlValidate = validateXml;
        var generation = xmlGeneration;
        var status = new NamedTextBlock { TextWrapping = TextWrapping.Wrap };
        xmlStatus = status;
        AutomationProperties.SetName(status, "XML well-formedness outcome for this exact clipboard snapshot");
        var button = new Button { Content = "Validate this snapshot as XML", IsEnabled = validateXml is not null };
        AutomationProperties.SetName(button, "Validate this snapshot as XML");
        button.Command = new AsyncCommand(() =>
        {
            var operation = ++xmlOperation;
            var capturedSnapshot = xmlSource;
            bool Current()
            {
                if (generation != xmlGeneration || operation != xmlOperation) { return false; }
                if (capturedSnapshot is null || xmlCurrent?.Invoke() != true) { ClearAndClose(); return false; }
                return generation == xmlGeneration && operation == xmlOperation && ReferenceEquals(xmlSource, capturedSnapshot);
            }
            if (!Current()) { return Task.CompletedTask; }
            status.Text = null;
            if (!Current()) { return Task.CompletedTask; }
            var validate = xmlValidate ?? throw new InvalidOperationException("Native XML checking is unavailable.");
            var result = validate(capturedSnapshot!, Current, CancellationToken.None);
            if (!Current()) { return Task.CompletedTask; }
            if (!ReferenceEquals(result.Source, capturedSnapshot))
            {
                throw new InvalidOperationException("XML result does not belong to this exact clipboard snapshot.");
            }
            status.Text = DescribeXmlResult(result);
            if (!Current()) { return Task.CompletedTask; }
            return Task.CompletedTask;
        }, exception =>
        {
            if (generation != xmlGeneration) { return; }
            if (xmlCurrent?.Invoke() != true) { ClearAndClose(); return; }
            if (generation != xmlGeneration) { return; }
            status.Text = "XML checking unavailable. No well-formedness result is claimed.";
            if (xmlCurrent?.Invoke() != true) { ClearAndClose(); return; }
            if (generation != xmlGeneration) { return; }
            reportFailure(exception);
        });
        chrome.Children.Add(button);
        chrome.Children.Add(new NamedTextBlock
        {
            Text = "Explicit XML well-formedness only; not schema validation, safety, trust, secret screening or uniqueness. "
                + "The complete unchanged snapshot is checked in memory, with no DTDs or external resources and at most 64 element levels. "
                + "An encoding declaration is text metadata, not evidence of the original clipboard encoding.",
            TextWrapping = TextWrapping.Wrap,
        });
        chrome.Children.Add(status);
    }

    private static string DescribeXmlResult(ClipboardXmlValidationResult result) => result.Outcome switch
    {
        ClipboardXmlOutcome.WellFormedXml => "WellFormedXML: this complete snapshot is one well-formed XML document.",
        ClipboardXmlOutcome.Invalid => $"Invalid XML: malformed or prohibited XML. Line {result.Line}, column {result.Column}.",
        ClipboardXmlOutcome.PolicyRefused when result.Reason == ClipboardXmlReason.ElementDepthExceeded =>
            $"PolicyRefused: element depth exceeds 64. Line {result.Line}, column {result.Column}.",
        ClipboardXmlOutcome.PolicyRefused => "PolicyRefused: the exact source is unavailable.",
        _ => throw new InvalidOperationException("Unknown XML checking outcome."),
    };

    private void ClearXmlValidation()
    {
        xmlGeneration++;
        xmlOperation++;
        if (xmlStatus is not null) { xmlStatus.Text = null; }
        xmlStatus = null;
        xmlSource = null;
        xmlCurrent = null;
        xmlValidate = null;
    }
}
