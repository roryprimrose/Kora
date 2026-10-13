using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

// Internal fault/interruption seam; production construction supplies no observer.
internal interface IHostInteractionTransactionCheckpoint
{
    void BeforeAudit(SqliteConnection connection, SqliteTransaction transaction);
    void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction);
    void BeforeAttachmentCopyVerification() { }
    void BeforeAttachmentMetadataRead(SqliteCommand command) { }
    void BeforeConversationDraftMetadataRead(SqliteCommand command) { }
}
