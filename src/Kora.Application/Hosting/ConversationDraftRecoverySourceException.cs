namespace Kora.Application.Hosting;

internal sealed class ConversationDraftRecoverySourceException(Exception innerException)
    : IOException("Exact saved draft source is unavailable or uncertain. Explicitly read/reconcile the source before recovery; raw buffer retained.", innerException);
