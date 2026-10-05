namespace Kora.Windows.Coordination;

internal sealed record InstanceMessage(
    int Version,
    string Kind,
    Guid Epoch = default,
    Guid Transaction = default,
    string Ticket = "",
    string Detail = "");
