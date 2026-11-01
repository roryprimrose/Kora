using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

public sealed class LocalConversationExecutionPreferences : IConversationExecutionPreferences
{
    private const string FileName = "conversation-execution-maximum.txt";
    private const string PendingFileName = "conversation-execution-maximum-unconfirmed.txt";
    private readonly IPreferenceStore store;

    public LocalConversationExecutionPreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalConversationExecutionPreferences(IPreferenceStore store) => this.store = store;

    public ConversationExecutionLimit? Load()
    {
        if (store.ReadText(PendingFileName) is not null)
        { throw new InvalidDataException("Conversation execution preference is unconfirmed. Inspect saved state and audit before repair."); }
        return ReadBack();
    }

    public ConversationExecutionLimit? ReadBack() =>
        store.ReadText(FileName) is { } text ? ConversationExecutionLimit.Parse(text) : null;

    public void BeginWrite() => store.WriteText(PendingFileName, "1");
    public void Save(ConversationExecutionLimit limit) => store.WriteText(FileName, limit.Serialize());
    public void ConfirmWrite() => store.Delete(PendingFileName);
}
