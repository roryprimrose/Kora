using Kora.Application.Configuration;
using Kora.Application.Infrastructure;
using Kora.Core.Auditing;
using Kora.Core.Configuration;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly AssistantNameConfigurationService assistantNameConfiguration;
    private bool isAssistantNameAvailable = true;

    public AsyncCommand ResetAssistantNameCommand { get; }
    public string AssistantNameConfigurationDescription => DescribeAssistantName(assistantNameConfiguration.Get());

    private void OnAssistantNameConfigurationChanged(object? sender, EventArgs args)
    {
        var current = assistantNameConfiguration.Get();
        if (!current.IsAvailable || !string.Equals(current.Name, AssistantName, StringComparison.Ordinal))
        {
            HoldVoiceInput("Microphone closed · display/PTT prefix changed or unavailable; use Enable listening");
        }
        uiDispatcher.Post(SynchronizeAssistantNameConfiguration);
    }

    private void SynchronizeAssistantNameConfiguration()
    {
        if (disposed) { return; }
        var current = assistantNameConfiguration.Get();
        isAssistantNameAvailable = current.IsAvailable;
        if (current.IsAvailable) { ApplyAssistantNameState(current.Name!); }
        else { AssistantNameSettingStatus = current.Recovery!; }
        OnPropertyChanged(nameof(AssistantNameConfigurationDescription));
        OnPropertyChanged(nameof(IsVoiceActivationAvailable));
        NotifyVoiceEnablementChanged();
    }

    public Task SetAssistantNameAsync(string value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser) =>
        ApplyAssistantNameProposalAsync(assistantNameConfiguration.Propose(value,
            assistantNameConfiguration.Get().Revision, CallPolicyRevision, OriginalOrigin(initiator), initiator));

    private Task ResetAssistantNameAsync() =>
        ApplyAssistantNameProposalAsync(assistantNameConfiguration.ProposeReset(
            assistantNameConfiguration.Get().Revision, CallPolicyRevision,
            OriginalOrigin(), SecurityAuditInitiator.LocalUser));

    private async Task RetireAssistantNameCaptureAsync()
    {
        var needsStop = IsVoiceEnabled || IsListening || !voiceRecognition.IsCaptureQuiescent;
        HoldVoiceInput("Microphone closed · display/PTT prefix changed; use Enable listening");
        if (needsStop) { await StopListeningAsync(); }
        if (!voiceRecognition.IsCaptureQuiescent)
        {
            throw new InvalidOperationException("Capture shutdown is not confirmed. Repair input before changing the name.");
        }
    }

    private async Task ApplyAssistantNameProposalAsync(AssistantNameProposal proposal,
        CancellationToken cancellationToken = default)
    {
        var result = await assistantNameConfiguration.ApplyAsync(proposal, communicationPolicy,
            () => IsCallMutationHostEligible && !IsBusy, RetireAssistantNameCaptureAsync, cancellationToken);
        SynchronizeAssistantNameConfiguration();
        if (!result.Succeeded)
        {
            AssistantNameSettingStatus = result.Error!;
            ShowFailure(string.Equals(result.Reason, "invalid-name", StringComparison.Ordinal) ? "The assistant name is invalid."
                : string.Equals(result.Reason, "capture-stop-failed", StringComparison.Ordinal) ? "The assistant name could not be changed."
                : "The assistant name could not be saved.", result.Error!);
        }
        else if (result.Changed)
        {
            ShowSuccess($"{AssistantName} is ready.",
                $"The display name, command prefix, and spoken identity now use {AssistantName}. Listening remains held until explicit recovery.");
        }
        else { AssistantNameInput = AssistantName; }
    }

    internal async Task ExecuteAssistantNameCommandAsync(
        AssistantNameCommand command, SecurityAuditInitiator initiator)
    {
        if (command.Operation == AppearanceCommandOperation.Clarify)
        {
            ShowInformation("Clarify the assistant setting.", command.Error!);
            return;
        }
        var current = assistantNameConfiguration.Get();
        if (command.Operation is AppearanceCommandOperation.List or AppearanceCommandOperation.Get)
        {
            ShowInformation("Assistant display/PTT command-prefix setting.",
                DescribeAssistantName(current) + Environment.NewLine + AssistantNameCommand.Syntax);
            return;
        }
        var proposal = command.Operation == AppearanceCommandOperation.Reset
            ? assistantNameConfiguration.ProposeReset(current.Revision, CallPolicyRevision, OriginalOrigin(initiator), initiator)
            : assistantNameConfiguration.Propose(command.Value!, current.Revision, CallPolicyRevision, OriginalOrigin(initiator), initiator);
        await ApplyAssistantNameProposalAsync(proposal);
    }

    private static string DescribeAssistantName(AssistantNameConfigurationState current) =>
        $"schema {AssistantNameOption.SchemaVersion}; {AssistantNameOption.Id} (spoken: {AssistantNameOption.SpokenName}) = {current.Name ?? "unavailable"}; "
        + $"{AssistantNameOption.Type}; default {AssistantNameOption.Default}; 1-{AssistantNameOption.MaximumWordCount} words, 1-{AssistantNameOption.MaximumLength} UTF-16 characters after whitespace normalization; "
        + $"{AssistantNameOption.Scope}; {AssistantNameOption.Effect}; {AssistantNameOption.Availability}; {AssistantNameOption.Confirmation}; {AssistantNameOption.ApplicationTiming}; reset: {AssistantNameOption.ResetEffect}; revision {current.Revision}; "
        + (current.IsSaved ? "source: saved device-local assistant-name preference. " : "source: unsaved domain default. ") + current.Recovery;
}
