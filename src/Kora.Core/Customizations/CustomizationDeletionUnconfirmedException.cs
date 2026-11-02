namespace Kora.Core.Customizations;

/// <summary>A deletion had an effect but its complete terminal outcome requires inspection.</summary>
public sealed class CustomizationDeletionUnconfirmedException(Exception innerException)
    : IOException("The exact source deletion committed, but readback, admission or audit is unconfirmed. "
        + "The source remains held. Inspect the source; do not retry or assume rollback.", innerException);
