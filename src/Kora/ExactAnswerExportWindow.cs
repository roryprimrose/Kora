using Avalonia.Controls;
using Kora.Application.Presentation;
using Kora.Core.Presentation;

namespace Kora;

internal sealed class ExactAnswerExportWindow : Window
{
    internal ExactAnswerExportView View { get; }
#pragma warning disable VSTHRD003 // Compatibility adapter forwards its own view's exact retained write operation.
    internal Task Operation => View.Operation;
#pragma warning restore VSTHRD003
    internal ExactAnswerExportOutcome? Outcome => View.Outcome;
    internal bool IsRetired => View.IsRetired;

    internal ExactAnswerExportWindow(ExactAnswerExportReview review, Func<bool> admitted,
        Action<string> reportFailure, CancellationToken token)
    {
        Title = "Export one exact retained ASSISTANT answer";
        Width = 820; Height = 720; MinWidth = 480; MinHeight = 400;
        View = new(this, review, admitted, reportFailure, token);
        Content = View;
        Opened += (_, _) => View.FocusSource();
    }

    internal void Release() => View.Release();
    internal static string Describe(ExactAnswerExportOutcome outcome, string path) => ExactAnswerExportView.Describe(outcome, path);
}
