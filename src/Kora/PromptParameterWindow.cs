using Avalonia.Controls;

using Kora.Application.Definitions;
using Kora.Core.Storage;

namespace Kora;

internal sealed class PromptParameterWindow : Window
{
    internal PromptParameterView View { get; }
    internal Dictionary<string, TextBox> Inputs => View.Inputs;
    internal Dictionary<string, Button> SpeakInputs => View.SpeakInputs;
    internal Button CancelVoice => View.CancelVoice;
    internal TextBlock VoiceStatus => View.VoiceStatus;
    internal Button Preview => View.Preview;
    internal Button Use => View.Use;
    internal TextBlock Expanded => View.Expanded;
    internal TextBlock Status => View.Status;
    internal ListBox Targets => View.Targets;
    internal Button RefreshTargets => View.RefreshTargets;
    internal Button NextTargets => View.NextTargets;
    internal Button ReviewSubmit => View.ReviewSubmit;
    internal Button ConfirmSubmit => View.ConfirmSubmit;
    internal Button CancelReview => View.CancelReview;
    internal TextBlock RetentionReview => View.RetentionReview;
    internal PromptParameterWindow(PromptParameterDraft draft, Action<string, string> edit, Action<string> run,
        Action<SessionWorkspaceEntry?>? select = null, Action<string>? speak = null, Action? cancelVoice = null)
    {
        Title = "Required prompt inputs — private preparation";
        Width = 880;
        Height = 720;
        View = new(this, draft, edit, run, static () => true, select, speak, cancelVoice);
        Content = View;
        View.CloseRequested += (_, _) => Close();
        Closed += (_, _) => View.Dispose();
    }
    internal void RetirePreview() => View.RetirePreview();
    internal void RetireReview() => View.RetireReview();
    internal void ClearPrivateText() { View.ClearPrivateText(); Content = null; }
}
