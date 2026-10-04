using Kora.Core.Configuration;

namespace Kora.Core.Commands;

public static class ModelApprovalSpeech
{
    private static readonly (string Phrase, ModelApprovalReply Reply)[] Phrases =
    [
        ("yes", ModelApprovalReply.Once),
        ("approve", ModelApprovalReply.Once),
        ("approve once", ModelApprovalReply.Once),
        ("allow this once", ModelApprovalReply.Once),
        ("yes for this session", ModelApprovalReply.Session),
        ("approve for this session", ModelApprovalReply.Session),
        ("allow this for this session", ModelApprovalReply.Session),
        ("always allow this", ModelApprovalReply.Always),
        ("approve always", ModelApprovalReply.Always),
        ("yes always", ModelApprovalReply.Always),
        ("no", ModelApprovalReply.Reject),
        ("reject", ModelApprovalReply.Reject),
        ("deny", ModelApprovalReply.Reject),
        ("do not allow", ModelApprovalReply.Reject),
    ];

    public static IEnumerable<string> GetPhrases(string assistantName)
    {
        var name = AssistantNameRules.Normalize(assistantName);
        return Phrases.SelectMany(item => new[] { item.Phrase, $"{name} {item.Phrase}" });
    }

    public static bool TryMatch(
        string transcript,
        string assistantName,
        bool requireAssistantName,
        out ModelApprovalReply reply)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transcript);
        var name = BuiltInCommandRouter.Normalize(AssistantNameRules.Normalize(assistantName));
        var normalized = BuiltInCommandRouter.Normalize(transcript);
        var prefix = $"{name} ";
        var prefixed = normalized.StartsWith(prefix, StringComparison.Ordinal);
        if (requireAssistantName && !prefixed)
        {
            reply = default;
            return false;
        }

        var phrase = prefixed ? normalized[prefix.Length..] : normalized;
        foreach (var item in Phrases)
        {
            if (string.Equals(
                    phrase,
                    BuiltInCommandRouter.Normalize(item.Phrase),
                    StringComparison.Ordinal))
            {
                reply = item.Reply;
                return true;
            }
        }

        reply = default;
        return false;
    }
}
