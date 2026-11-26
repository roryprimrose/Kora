using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

public sealed class SessionPermissionDefaultPreferences
{
    private const string FileName = "session-permission-default.txt";
    private readonly IPreferenceStore store;

    public SessionPermissionDefaultPreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal SessionPermissionDefaultPreferences(IPreferenceStore store) => this.store = store;

    public SessionPermissionLevel Load()
    {
        var value = store.ReadText(FileName, 32);
        return value switch
        {
            null or "review-all" => SessionPermissionLevel.ReviewAll,
            "model-review" => SessionPermissionLevel.ModelReview,
            "approve-all" => SessionPermissionLevel.ApproveAll,
            _ => throw new InvalidDataException("The saved default session permission level is malformed."),
        };
    }

    public void Save(SessionPermissionLevel level)
    {
        var value = level switch
        {
            SessionPermissionLevel.ReviewAll => "review-all",
            SessionPermissionLevel.ModelReview => "model-review",
            SessionPermissionLevel.ApproveAll => "approve-all",
            _ => throw new ArgumentOutOfRangeException(nameof(level)),
        };
        store.WriteText(FileName, value);
    }
}
