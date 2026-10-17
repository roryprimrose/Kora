using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core.Artifacts;
using Kora.Core.Configuration;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly ArtifactDirectoryConfigurationService? artifactDirectoryConfiguration;
    private readonly McpServerConfigurationService? mcpServerConfiguration;
    private IReadOnlyList<string> additionalArtifactDirectories = [];
    private IReadOnlyList<McpServerConfigurationSummary> mcpServers = [];
    private string artifactDirectoryInput = string.Empty;
    private string? selectedArtifactDirectory;
    private McpServerConfigurationSummary? selectedMcpServer;
    private ArtifactSourceKind selectedArtifactDirectoryKind = ArtifactSourceKind.Skill;
    private bool artifactDirectoryControlActive;
    public Task ArtifactChangeUpdate { get; private set; } = Task.CompletedTask;

    private void OnArtifactCatalogueChanged(object? sender, EventArgs args) =>
        uiDispatcher.Post(() => new AsyncCommand(() => ArtifactChangeUpdate = ApplyArtifactCatalogueChangeAsync(),
            HandleCommandException).Execute(null));

    private void OnArtifactCatalogueReloadFailed(
        object? sender,
        ArtifactCatalogueReloadFailedEventArgs args) =>
        uiDispatcher.Post(() => ShowFailure(
            "Artifact changes could not be loaded.",
            "Owned instructions are withheld until a successful reload and exact revision review. "
            + "Other historical catalogue entries may remain available; no managed source is claimed current."));

    private void OnMcpServerConfigurationChanged(object? sender, EventArgs args) =>
        uiDispatcher.Post(() => RefreshArtifactDirectories(publishConfigurationChange: false));

    private async Task ApplyArtifactCatalogueChangeAsync()
    {
        var cancellation = Task.CompletedTask;
        var retirement = Task.CompletedTask;
        RefreshArtifactCommandOptions();
        var retiredInteraction = false;
        if (activeReasoningArtifact is { } running
            && !artifactCatalogue.IsCapturedRevisionCurrent(running.Id, running.Digest))
        {
            cancellation = activeReasoningCancellation?.CancelAsync() ?? Task.CompletedTask;
            retiredInteraction = true;
        }
        if (pendingQuestionArtifact is { } question
            && !artifactCatalogue.IsCurrent(question.Id, question.Digest))
        {
            ClearPendingModelQuestion();
            retiredInteraction = true;
        }
        if (pendingModelActionArtifact is { } action
            && !artifactCatalogue.IsCurrent(action.Id, action.Digest))
        {
            retirement = ClearPendingModelActionAsync("artifact-revision-changed");
            retiredInteraction = true;
        }
        if (retiredInteraction)
        {
            ShowInformation(
                "Artifact changed.",
                "The previous request, question, or approval was retired. Start a new request to use the reloaded artifact revision.");
        }
        await Task.WhenAll(cancellation, retirement);
    }

    public AsyncCommand RefreshArtifactDirectoriesCommand { get; }
    public AsyncCommand AddArtifactDirectoryCommand { get; }
    public AsyncCommand RemoveArtifactDirectoryCommand { get; }
    public AsyncCommand IncludeMcpServerCommand { get; }
    public AsyncCommand ExcludeMcpServerCommand { get; }
    public IReadOnlyList<ArtifactSourceKind> ArtifactDirectoryKinds { get; } = Enum.GetValues<ArtifactSourceKind>();

    public ArtifactSourceKind SelectedArtifactDirectoryKind
    {
        get => selectedArtifactDirectoryKind;
        set
        {
            if (SetProperty(ref selectedArtifactDirectoryKind, value))
            {
                SelectedArtifactDirectory = null;
                OnPropertyChanged(nameof(IsMcpConfigurationSelected));
                OnPropertyChanged(nameof(ArtifactPathKind));
                RefreshArtifactDirectories();
            }
        }
    }

    public bool IsMcpConfigurationSelected =>
        SelectedArtifactDirectoryKind == ArtifactSourceKind.McpServerConfiguration;

    public string ArtifactPathKind => IsMcpConfigurationSelected ? "file" : "directory";

    public IReadOnlyList<McpServerConfigurationSummary> McpServers
    {
        get => mcpServers;
        private set => SetProperty(ref mcpServers, value);
    }

    public McpServerConfigurationSummary? SelectedMcpServer
    {
        get => selectedMcpServer;
        set
        {
            if (SetProperty(ref selectedMcpServer, value))
            {
                IncludeMcpServerCommand.NotifyCanExecuteChanged();
                ExcludeMcpServerCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IReadOnlyList<string> AdditionalArtifactDirectories
    {
        get => additionalArtifactDirectories;
        private set => SetProperty(ref additionalArtifactDirectories, value);
    }

    public string ArtifactDirectoryInput
    {
        get => artifactDirectoryInput;
        set
        {
            if (SetProperty(ref artifactDirectoryInput, value))
            {
                AddArtifactDirectoryCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? SelectedArtifactDirectory
    {
        get => selectedArtifactDirectory;
        set
        {
            if (SetProperty(ref selectedArtifactDirectory, value))
            {
                RemoveArtifactDirectoryCommand.NotifyCanExecuteChanged();
                IncludeMcpServerCommand.NotifyCanExecuteChanged();
                ExcludeMcpServerCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanChangeArtifactDirectories =>
        artifactDirectoryConfiguration is not null
        && !disposed
        && IsCallMutationHostEligible
        && !artifactDirectoryControlActive
        && !IsResponseInteractionPending;

    public string ArtifactDirectoryStatus =>
        artifactDirectoryConfiguration is null
            ? "Additional artifact directory configuration is unavailable."
            : AdditionalArtifactDirectories.Count == 0
                    ? $"No additional {SelectedArtifactDirectoryKind.ToString().ToLowerInvariant()} {(IsMcpConfigurationSelected ? "files" : "directories")} are configured."
                    : $"{AdditionalArtifactDirectories.Count} additional {SelectedArtifactDirectoryKind.ToString().ToLowerInvariant()} {(AdditionalArtifactDirectories.Count == 1 ? ArtifactPathKind : IsMcpConfigurationSelected ? "files" : "directories")} configured.";

    private Task RefreshArtifactDirectoriesAsync()
    {
        RefreshArtifactDirectories();
        return Task.CompletedTask;
    }

    private void RefreshArtifactDirectories(bool publishConfigurationChange = true)
    {
        if (artifactDirectoryConfiguration is null)
        {
            AdditionalArtifactDirectories = [];
            NotifyArtifactDirectoryControlChanged();
            return;
        }
        try
        {
            var settings = publishConfigurationChange
                ? artifactDirectoryConfiguration.Load()
                : artifactDirectoryConfiguration.ReadCurrent();
            AdditionalArtifactDirectories = settings.GetPaths(SelectedArtifactDirectoryKind);
            McpServers = mcpServerConfiguration?.Load() ?? [];
            if (SelectedArtifactDirectory is not null
                && !AdditionalArtifactDirectories.Contains(SelectedArtifactDirectory, StringComparer.OrdinalIgnoreCase))
            {
                SelectedArtifactDirectory = null;
            }
            NotifyArtifactDirectoryControlChanged();
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or ArgumentException
            or NotSupportedException)
        {
            ApplicationLog.Error(logger, exception, "Reading additional artifact and MCP sources");
            ShowFailure("Additional artifact and MCP sources are unavailable.", exception.Message);
        }
    }

    private Task AddArtifactDirectoryAsync()
    {
        ChangeArtifactDirectory(add: true);
        return Task.CompletedTask;
    }

    private Task RemoveArtifactDirectoryAsync()
    {
        ChangeArtifactDirectory(add: false);
        return Task.CompletedTask;
    }

    private Task SetSelectedMcpServerIncludedAsync(bool included)
    {
        if (!CanChangeArtifactDirectories || SelectedMcpServer is null)
        {
            return Task.CompletedTask;
        }
        try
        {
            var selectedId = SelectedMcpServer.Id;
            artifactDirectoryConfiguration!.SetMcpServerIncluded(selectedId, included);
            McpServers = mcpServerConfiguration?.Load() ?? [];
            SelectedMcpServer = McpServers.SingleOrDefault(server =>
                string.Equals(server.Id, selectedId, StringComparison.Ordinal));
            OnPropertyChanged(nameof(ArtifactDirectoryStatus));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException)
        {
            ApplicationLog.Error(logger, exception, "Changing MCP server inclusion");
            ShowFailure("MCP server inclusion was not changed.", exception.Message);
        }
        return Task.CompletedTask;
    }

    private void ChangeArtifactDirectory(bool add)
    {
        if (!CanChangeArtifactDirectories)
        {
            return;
        }
        artifactDirectoryControlActive = true;
        NotifyArtifactDirectoryControlChanged();
        try
        {
            var settings = add
                ? artifactDirectoryConfiguration!.Add(SelectedArtifactDirectoryKind, ArtifactDirectoryInput)
                : artifactDirectoryConfiguration!.Remove(SelectedArtifactDirectoryKind, SelectedArtifactDirectory!);
            AdditionalArtifactDirectories = settings.GetPaths(SelectedArtifactDirectoryKind);
            ArtifactDirectoryInput = string.Empty;
            SelectedArtifactDirectory = null;
            OnPropertyChanged(nameof(ArtifactDirectoryStatus));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException)
        {
            ApplicationLog.Error(logger, exception, "Changing additional artifact and MCP sources");
            ShowFailure("Additional artifact and MCP sources were not changed.", exception.Message);
        }
        finally
        {
            artifactDirectoryControlActive = false;
            NotifyArtifactDirectoryControlChanged();
        }
    }

    private void NotifyArtifactDirectoryControlChanged()
    {
        OnPropertyChanged(nameof(CanChangeArtifactDirectories));
        OnPropertyChanged(nameof(ArtifactDirectoryStatus));
        RefreshArtifactDirectoriesCommand.NotifyCanExecuteChanged();
        AddArtifactDirectoryCommand.NotifyCanExecuteChanged();
        RemoveArtifactDirectoryCommand.NotifyCanExecuteChanged();
    }
}
