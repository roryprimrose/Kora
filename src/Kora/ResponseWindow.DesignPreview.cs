#if DEBUG
using System.Collections.Generic;

using Avalonia.Controls;
using Avalonia.Threading;

namespace Kora;

public sealed partial class ResponseWindow
{
    // Reserved for the XAML previewer, which constructs this window without application services.
    // Runtime and tests construct it with a MainViewModel, so no service wiring is performed here.
    public ResponseWindow()
    {
        if (!Design.IsDesignMode)
        {
            throw new InvalidOperationException(
                "ResponseWindow requires a MainViewModel; the parameterless constructor is for the XAML previewer only.");
        }

        viewModel = null!;
        pushToTalk = null!;
        responseTimeoutTimer = new DispatcherTimer();
        positionSaveTimer = new DispatcherTimer();
        responseInactivity = new(TimeProvider.System);
        getWorkingArea = static () => null;
        InitializeComponent();
        DataContext = new DesignPreviewData();
    }

    // Reflection bindings leave IsVisible at its default (true) when a source property is missing,
    // so every visibility flag bound by ResponseWindow.axaml is declared here.
    private sealed class DesignPreviewData
    {
        public string AssistantName => "Kora";
        public string ResponseBody => "Expect sunshine through the morning with light cloud after lunch. The high is 22 degrees "
                + "with a gentle easterly breeze, and there is no rain in the forecast.";
        public string CommandText => string.Empty;
        public string CommandInputPlaceholder => "Type a command or / for artifacts";
        public string CommandSubmitLabel => "Run";

        public string PendingQuestionNavigationStatus => string.Empty;
        public string SubmittedWorkActivityStatus => string.Empty;
        public string ReadCurrentResponseStatus => string.Empty;
        public string ResponseReadProposalReview => string.Empty;
        public string WebResultDetailStatus => string.Empty;
        public string WebSummaryStatus => string.Empty;
        public string WebSummaryText => string.Empty;

        public bool CanCompose => true;
        public bool IsPushToTalkVisible => true;
        public bool IsResponseWindowTopmost => false;
        public bool IsCancelTaskVisible => false;
        public bool IsSpeaking => false;
        public bool IsDetailsOfferPending => false;
        public bool IsArtifactCommandDropdownVisible => false;
        public bool IsResponseInteractionPending => false;
        public bool HasResponseReadProposal => false;
        public bool HasResponseActions => false;
        public bool HasWebResultDetails => false;

        public IReadOnlyList<object> ResponseActions { get; } = [];
        public IReadOnlyList<object> WebResultDetailActions { get; } = [];
        public IReadOnlyList<object> WebSummaryConfirmations { get; } = [];
        public IReadOnlyList<object> ArtifactCommandOptions { get; } = [];
    }
}
#endif
