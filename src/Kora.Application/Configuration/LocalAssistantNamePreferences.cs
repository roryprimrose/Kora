using Kora.Application.Diagnostics;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed class LocalAssistantNamePreferences : IAssistantNamePreferences
{
    private const string FileName = "assistant-name.txt";
    private readonly IPreferenceStore store;
    private readonly ILogger<LocalAssistantNamePreferences> logger;

    public LocalAssistantNamePreferences(
        IApplicationDataPaths paths,
        ILogger<LocalAssistantNamePreferences> logger)
        : this(new LocalPreferenceStore(paths), logger)
    {
    }

    internal LocalAssistantNamePreferences(
        IPreferenceStore store,
        ILogger<LocalAssistantNamePreferences> logger)
    {
        this.store = store;
        this.logger = logger;
    }

    public string? LoadName()
    {
        var contents = store.ReadText(FileName);
        if (contents is null)
        {
            return null;
        }

        try
        {
            var name = AssistantNameRules.Normalize(contents);
            ApplicationLog.AssistantNamePreferenceLoaded(logger);
            return name;
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The saved assistant name preference is invalid.", exception);
        }
    }

    public void SaveName(string name)
    {
        var normalizedName = AssistantNameRules.Normalize(name);
        store.WriteText(FileName, normalizedName);
        ApplicationLog.AssistantNamePreferenceSaved(logger);
    }
}
