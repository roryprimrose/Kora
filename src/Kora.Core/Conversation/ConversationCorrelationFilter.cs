using System.Runtime.InteropServices;

using Kora.Core.Hosting;

namespace Kora.Core.Conversation;

/// <summary>Passive exact correlation metadata within an already host-resolved session, never authority.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ConversationCorrelationFilter(
    HostId<RequestIdentity>? RequestId = null, HostId<TaskIdentity>? TaskId = null)
{
    public void Validate()
    {
        RequestId?.Validate();
        TaskId?.Validate();
    }

    public bool Matches(ConversationMessage message) =>
        (RequestId is not { } request || message.Request.RequestId == request)
        && (TaskId is not { } task || message.Request.TaskId == task);

    public static ConversationCorrelationFilter Parse(string requestId, string taskId) =>
        new(ParseOptional<RequestIdentity>(requestId), ParseOptional<TaskIdentity>(taskId));

    private static HostId<T>? ParseOptional<T>(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) { return null; }
        if (!Guid.TryParseExact(text, "D", out var value) || value == Guid.Empty
            || !string.Equals(text, value.ToString("D"), StringComparison.Ordinal))
        { throw new InvalidDataException("Enter a nonempty canonical lowercase D-format GUID; empty means Any. No whitespace or repair."); }
        return new(value);
    }
}
