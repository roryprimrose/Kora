using Kora.Core.Authorization;
using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed class StartupPresentationReview
{
    internal StartupPresentationReview(StartupPresentation? value, StartupPresentation? saved,
        WorkSessionAuthorization session, Func<bool> eligible, long callRevision, DateTimeOffset expires)
    { Value = value; Saved = saved; Session = session; Eligible = eligible; CallRevision = callRevision; Expires = expires; }
    internal StartupPresentation? Value { get; }
    internal StartupPresentation? Saved { get; }
    internal WorkSessionAuthorization Session { get; }
    internal Func<bool> Eligible { get; }
    internal long CallRevision { get; }
    internal DateTimeOffset Expires { get; }
    public string Description => $"Review: {Describe(Value)}. Last confirmed intent: {Describe(Saved)}. "
        + "No change yet. Confirm separately in this native Settings view within 60 seconds, or Cancel. "
        + "Only the next supported manual launch is affected; logon and argument-free launches are handled separately and keep existing behavior. "
        + "No launch now, focus activation, conversation selection/body read, unsent text restoration, listening, provider or permission change.";
    internal static string Describe(StartupPresentation? value) => value switch
    {
        null => "existing initialization (no saved override)",
        StartupPresentation.Tray => "tray/minimal shell (no workspace)",
        StartupPresentation.Workspace => "existing Main workspace, without loading a conversation",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
