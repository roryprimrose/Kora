using System.Collections.Concurrent;
using System.Diagnostics.Tracing;

namespace Kora.Rt2;

internal sealed class DiagnosticObserver : EventListener
{
    private readonly ConcurrentDictionary<string, int> events = new(StringComparer.Ordinal);
    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name is "System.Net.Http" or "System.Net.Sockets")
            EnableEvents(eventSource, EventLevel.Informational);
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        // Payloads can include content and credentials. Retain only fixed event
        // identities/counters from these two managed sources, never payloads.
        events?.AddOrUpdate(eventData.EventSource.Name + ":" + eventData.EventId, 1, (_, count) => count + 1);
    }

    internal IReadOnlyDictionary<string, int> Counts => events;
}
