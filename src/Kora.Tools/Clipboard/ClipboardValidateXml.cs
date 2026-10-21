using Kora.Core.Context;

namespace Kora.Tools.Clipboard;

public sealed class ClipboardValidateXml(ClipboardSnapshotBroker broker)
{
    public ClipboardXmlValidationResult Execute(ClipboardSnapshot snapshot, Func<bool> canPresent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool IsCurrent() => canPresent() && ReferenceEquals(broker.Current, snapshot) && canPresent()
            && ReferenceEquals(broker.Current, snapshot);
        ClipboardXmlValidationResult Refused() =>
            new(snapshot, ClipboardXmlOutcome.PolicyRefused, ClipboardXmlReason.SourceUnavailable, 0, 0);
        if (!IsCurrent()) { return Refused(); }
        var result = ClipboardXmlPolicy.Validate(snapshot, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var current = IsCurrent();
        cancellationToken.ThrowIfCancellationRequested();
        return current ? result : Refused();
    }
}
