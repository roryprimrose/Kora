using System.Globalization;

using Kora.Core.Interaction;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private static string ConversationNoticeDetail(LocalEventView notice) =>
        notice.Event.Summary + "\nExact task: " + notice.Event.TaskId!.Value.Value.ToString("D")
        + "\nObserved status: " + notice.Event.Type + "; delivery: " + notice.Reason
        + "\nOriginal expiry: " + notice.Event.ExpiresAt.ToString("O", CultureInfo.InvariantCulture)
        + "\nInspection only. The selected conversation, work selection and voice target are unchanged. "
        + "Read the exact task in Work for its retained status; answer any still-current question separately in Questions. "
        + "No answer, approval, dispatch, retry, speech or deadline extension is authorised by a notice.";
}
