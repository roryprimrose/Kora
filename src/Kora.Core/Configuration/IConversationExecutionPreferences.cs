namespace Kora.Core.Configuration;

/// <summary>Device-local future ordinary-conversation preferences with unconfirmed-write recovery.</summary>
public interface IConversationExecutionPreferences
{
    /// <summary>Loads confirmed saved demand; absence means the domain default, not qualified capacity.</summary>
    ConversationExecutionLimit? Load();
    /// <summary>Reads the persisted value without clearing an unconfirmed-write marker.</summary>
    ConversationExecutionLimit? ReadBack();
    /// <summary>Marks a write unconfirmed before changing its value.</summary>
    void BeginWrite();
    /// <summary>Persists exact validated demand.</summary>
    void Save(ConversationExecutionLimit limit);
    /// <summary>Confirms a read-back and audited write.</summary>
    void ConfirmWrite();
}
