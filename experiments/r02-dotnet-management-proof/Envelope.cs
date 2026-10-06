using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kora.Mg1;

internal static class Envelope
{
    internal const int InputBytes = 32768;
    internal const int OutputBytes = 4096;
    internal const int DeadlineMs = 15000;
    internal const int AttemptsPerHour = 30;
    internal const string System = "MG1_SYSTEM: synthetic status proposals only; no tools, identity or approval authority.";
    internal const string Target = "host-owned-synthetic-task";
    internal const string Credential = "RT1_SYNTHETIC_NOT_A_CREDENTIAL";

    internal static void Select(string prompt, string history)
    {
        var selected = JsonSerializer.Serialize(new { system = System, prompt, history });
        if (Encoding.UTF8.GetByteCount(selected) > InputBytes)
            throw new InvalidDataException("selected-context-overflow");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record Proposal(string Operation, string Target, int Revision, string Text);

internal sealed class ProvisionalOutput
{
    private static readonly JsonSerializerOptions options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true
    };
    private readonly StringBuilder text = new();
    private bool complete;
    private bool overflow;
    internal int Bytes => Encoding.UTF8.GetByteCount(text.ToString());
    internal void Append(string delta)
    {
        if (complete) throw new InvalidOperationException("output-already-complete");
        if (overflow) throw new InvalidDataException("output-overflow");
        text.Append(delta);
        if (Bytes > Envelope.OutputBytes)
        {
            overflow = true;
            throw new InvalidDataException("output-overflow");
        }
    }
    internal Proposal Complete()
    {
        if (complete) throw new InvalidOperationException("output-already-complete");
        complete = true;
        if (Bytes > Envelope.OutputBytes) throw new InvalidDataException("output-overflow");
        using var document = JsonDocument.Parse(text.ToString());
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("proposal-not-object");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
            if (!names.Add(property.Name)) throw new InvalidDataException("duplicate-proposal-field");
        var proposal = JsonSerializer.Deserialize<Proposal>(text.ToString(), options)
            ?? throw new InvalidDataException("null-proposal");
        if (proposal.Operation != "status" || proposal.Target != Envelope.Target || proposal.Revision != 7
            || string.IsNullOrWhiteSpace(proposal.Text))
            throw new InvalidDataException("invalid-proposal-target-operation-revision");
        return proposal;
    }
}

internal sealed class Admission(TimeProvider time)
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, Queue<long>> attempts = new(StringComparer.Ordinal);
    private Guid? owner;
    internal bool Quarantined { get; private set; }
    internal Guid Admit(string profile)
    {
        lock (gate)
        {
            if (owner is not null) throw new InvalidOperationException(Quarantined ? "termination-unknown" : "management-busy");
            if (!attempts.TryGetValue(profile, out var window))
                attempts.Add(profile, window = new Queue<long>());
            var now = time.GetTimestamp();
            while (window.TryPeek(out var first) && time.GetElapsedTime(first, now) >= TimeSpan.FromHours(1)) window.Dequeue();
            if (window.Count >= Envelope.AttemptsPerHour) throw new InvalidOperationException("rolling-hour-budget");
            window.Enqueue(now);
            owner = Guid.NewGuid();
            return owner.Value;
        }
    }
    internal void End(Guid request, bool observedTermination)
    {
        lock (gate)
        {
            if (owner != request) throw new InvalidOperationException("host-request-mismatch");
            if (observedTermination) { owner = null; Quarantined = false; }
            else Quarantined = true;
        }
    }
    internal string LocalStatus()
    {
        lock (gate) return Quarantined ? "Unknown; Queue / Replace / Cancel available" : "Queue / Replace / Cancel available";
    }
}

internal sealed class ManualTime : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UnixEpoch;
    private long timestamp;
    public override DateTimeOffset GetUtcNow() => now;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => timestamp;
    internal void Advance(TimeSpan delta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);
        now += delta;
        timestamp += delta.Ticks;
    }
    internal void JumpUtc(TimeSpan delta) => now += delta;
}
