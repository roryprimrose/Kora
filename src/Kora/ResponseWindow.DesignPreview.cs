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

    // Change this to preview another compact-window state; C# edits require a previewer rebuild.
    private const DesignPreviewScenario Scenario = DesignPreviewScenario.PermissionRequest;

    private enum DesignPreviewScenario
    {
        Response,
        PermissionRequest,
    }

    // Reflection bindings leave IsVisible at its default (true) when a source property is missing,
    // so every visibility flag bound by ResponseWindow.axaml is declared here.
    private sealed class DesignPreviewData
    {
        private static bool IsPermissionRequest => Scenario == DesignPreviewScenario.PermissionRequest;

        public string AssistantName => "Kora";
        public string ResponseBody => IsPermissionRequest
            ? "I can open Settings so you can change the theme. Opening it is an action, so I need your approval first."
            : "Expect sunshine through the morning with light cloud after lunch. The high is 22 degrees "
                + "with a gentle easterly breeze, and there is no rain in the forecast.";
        public string CommandText => string.Empty;
        public string CommandInputPlaceholder => "Type a command or / for artifacts";
        public string CommandSubmitLabel => "Run";

        public string PendingQuestionNavigationStatus => string.Empty;
        public string ModelActionReviewStatus => string.Empty;
        public string ModelActionReviewTitle => IsPermissionRequest ? "Allow Kora to open Kora settings?" : string.Empty;
        public string ModelActionReviewDetail => string.Empty;
    public string ModelActionReviewCommand => IsPermissionRequest ? "open settings" : string.Empty;
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
        public bool IsGrantEditorVisible => false;
        public bool IsGrantChangePending => false;
        public bool IsModelQuestionPending => false;
        public bool IsArtifactCommandDropdownVisible => false;
        public bool IsResponseInteractionPending => IsPermissionRequest;
        public bool CanConfirmModelActionReview => IsPermissionRequest;
        public bool CanConfirmReusableModelActionReview => IsPermissionRequest;
        public bool CanRejectModelActionReview => IsPermissionRequest;
        public bool HasResponseReadProposal => false;
        public bool HasResponseActions => false;
        public bool HasWebResultDetails => false;

        public IReadOnlyList<object> ResponseActions { get; } = [];
        public IReadOnlyList<object> WebResultDetailActions { get; } = [];
        public IReadOnlyList<object> WebSummaryConfirmations { get; } = [];
        public IReadOnlyList<object> ModelActionReviews { get; } = IsPermissionRequest ? [new object()] : [];
    public object? PendingModelActionReview => ModelActionReviews.Count > 0 ? ModelActionReviews[0] : null;
        public IReadOnlyList<object> ModelQuestionChoices { get; } = [];
        public IReadOnlyList<object> ArtifactCommandOptions { get; } = [];
        public IReadOnlyList<object> GrantActions { get; } = [];
        public IReadOnlyList<object> GrantOperations { get; } = [];
        public IReadOnlyList<object> GrantScopes { get; } = [];
    }
}
#endif
