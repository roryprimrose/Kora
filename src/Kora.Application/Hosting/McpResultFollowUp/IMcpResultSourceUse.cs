using Kora.Core.Hosting;

namespace Kora.Application.Hosting.McpResultFollowUp;

/// <summary>
/// Independent cached source/account/permission/call/privacy/ownership and versioned source-use risk admission.
/// A completed invocation (including its spent grant) is evidence, never model-use consent.
/// Implementations must not reconnect, reread the source, renew a grant or infer permission from its text.
/// Both the original source channel and new question channel must remain admitted.
/// </summary>
internal interface IMcpResultSourceUse
{
    McpResultSourceUseDecision Check(McpResultObservation observation, string question, RequestOrigin questionOrigin,
        CancellationToken token);
}
