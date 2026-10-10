using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Network;
using Kora.Tools.Network;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly PreapprovedUriList? preapprovedUriList;
    private readonly PreapprovedUriAdd? preapprovedUriAdd;
    private readonly PreapprovedUriRemove? preapprovedUriRemove;
    private readonly PreapprovedUriClear? preapprovedUriClear;
    private IReadOnlyList<string> preapprovedUriPatterns = [];
    private string preapprovedUriInput = string.Empty;
    private string? selectedPreapprovedUri;
    private bool preapprovedUriControlActive;

    public AsyncCommand RefreshPreapprovedUrisCommand { get; }
    public AsyncCommand AddPreapprovedUriCommand { get; }
    public AsyncCommand RemovePreapprovedUriCommand { get; }
    public AsyncCommand ClearPreapprovedUrisCommand { get; }

    public IReadOnlyList<string> PreapprovedUriPatterns
    {
        get => preapprovedUriPatterns;
        private set => SetProperty(ref preapprovedUriPatterns, value);
    }

    public string PreapprovedUriInput
    {
        get => preapprovedUriInput;
        set
        {
            if (SetProperty(ref preapprovedUriInput, value))
            {
                AddPreapprovedUriCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? SelectedPreapprovedUri
    {
        get => selectedPreapprovedUri;
        set
        {
            if (SetProperty(ref selectedPreapprovedUri, value))
            {
                RemovePreapprovedUriCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanChangePreapprovedUris =>
        preapprovedUriList is not null
        && preapprovedUriAdd is not null
        && preapprovedUriRemove is not null
        && preapprovedUriClear is not null
        && !disposed
        && IsCallMutationHostEligible
        && !preapprovedUriControlActive
        && !IsResponseInteractionPending;

    public string PreapprovedUriStatus =>
        preapprovedUriList is null
            ? "Preapproved address configuration is unavailable."
            : PreapprovedUriPatterns.Count == 0
                ? "No addresses are preapproved. Model-originated retrieval still requires an explicit permission grant."
                : $"{PreapprovedUriPatterns.Count} address pattern(s) are preapproved.";

    private Task RunNativePreapprovedUriCommandAsync(
        PreapprovedUriCommandOperation operation,
        string? pattern = null) =>
        ExecutePreapprovedUriCommandAsync(
            new(operation, pattern),
            SecurityAuditInitiator.LocalUser);

    internal Task ExecutePreapprovedUriCommandAsync(
        PreapprovedUriCommand command,
        SecurityAuditInitiator initiator)
    {
        if (!CanChangePreapprovedUris)
        {
            if (!disposed)
            {
                Transcript = "Preapproved address control requires the current owning unlocked host and no pending question or approval.";
            }
            return Task.CompletedTask;
        }
        if (command.Operation == PreapprovedUriCommandOperation.Clarify)
        {
            ShowFailure("Clarify the preapproved address setting.", command.Error!);
            return Task.CompletedTask;
        }

        preapprovedUriControlActive = true;
        NotifyPreapprovedUriControlChanged();
        try
        {
            var settings = command.Operation switch
            {
                PreapprovedUriCommandOperation.List => preapprovedUriList!.Execute(),
                PreapprovedUriCommandOperation.Add => preapprovedUriAdd!.Execute(command.Pattern!, initiator),
                PreapprovedUriCommandOperation.Remove => preapprovedUriRemove!.Execute(command.Pattern!, initiator),
                PreapprovedUriCommandOperation.Clear => preapprovedUriClear!.Execute(initiator),
                _ => throw new ArgumentOutOfRangeException(nameof(command)),
            };
            PreapprovedUriPatterns = settings.Patterns;
            if (command.Operation == PreapprovedUriCommandOperation.Add)
            {
                PreapprovedUriInput = string.Empty;
            }
            if (SelectedPreapprovedUri is not null
                && !settings.Patterns.Contains(SelectedPreapprovedUri, StringComparer.Ordinal))
            {
                SelectedPreapprovedUri = null;
            }
            OnPropertyChanged(nameof(PreapprovedUriStatus));
            var body = settings.Patterns.Count == 0
                ? "No addresses are preapproved."
                : string.Join(Environment.NewLine, settings.Patterns);
            PresentResponse(
                AssistantState.Information,
                command.Operation == PreapprovedUriCommandOperation.List
                    ? "Preapproved addresses."
                    : "Preapproved addresses changed.",
                body,
                refreshOutput: false);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException)
        {
            ApplicationLog.Error(logger, exception, "Inspecting or changing preapproved URI patterns");
            ShowFailure("Preapproved addresses were not changed.", exception.Message);
        }
        finally
        {
            preapprovedUriControlActive = false;
            NotifyPreapprovedUriControlChanged();
        }
        return Task.CompletedTask;
    }

    private void NotifyPreapprovedUriControlChanged()
    {
        OnPropertyChanged(nameof(CanChangePreapprovedUris));
        OnPropertyChanged(nameof(PreapprovedUriStatus));
        RefreshPreapprovedUrisCommand.NotifyCanExecuteChanged();
        AddPreapprovedUriCommand.NotifyCanExecuteChanged();
        RemovePreapprovedUriCommand.NotifyCanExecuteChanged();
        ClearPreapprovedUrisCommand.NotifyCanExecuteChanged();
    }
}
