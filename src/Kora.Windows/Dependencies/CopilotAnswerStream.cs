using System.Text;
using System.Threading.Channels;
using Microsoft.Copilot;
using Kora.Core.Dependencies;

namespace Kora.Windows.Dependencies;

internal sealed class CopilotAnswerStream
{
    private readonly Lock gate = new();
    private readonly StringBuilder text = new();
    private readonly HashSet<Guid> events = [];
    private readonly Channel<string> snapshots = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropOldest,
        AllowSynchronousContinuations = false,
    });
    private string? messageId;
    private bool closed;

    internal void Observe(AssistantMessageDeltaEvent delta)
    {
        lock (gate)
        {
            if (closed) { return; }
            try
            {
                if (!string.IsNullOrEmpty(delta.AgentId)
                    || string.IsNullOrWhiteSpace(delta.Data.MessageId)
                    || messageId is not null && !string.Equals(messageId, delta.Data.MessageId, StringComparison.Ordinal))
                { throw new InvalidDataException("Copilot answer delta belongs to another message or agent."); }
                if (string.IsNullOrEmpty(delta.Data.DeltaContent) || !events.Add(delta.Id)) { return; }
                messageId = delta.Data.MessageId;
                if (delta.Data.DeltaContent.Length > ModelToolProtocol.MaximumOutputUtf8Bytes - text.Length)
                { throw new InvalidDataException("Copilot answer delta exceeds its character bound."); }
                text.Append(delta.Data.DeltaContent);
                var answer = text.ToString();
                // A JSON delta can end between a surrogate pair; never publish incomplete Unicode.
                var complete = char.IsHighSurrogate(answer[^1]) ? answer[..^1] : answer;
                if (new UTF8Encoding(false, true).GetByteCount(complete) > ModelToolProtocol.MaximumOutputUtf8Bytes
                    || complete.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
                { throw new InvalidDataException("Copilot provisional answer exceeds its content bound."); }
                // Coalesce snapshots, not deltas: a slow UI still receives the complete latest prefix.
                if (complete.Length != 0) { snapshots.Writer.TryWrite(complete); }
            }
            catch (EncoderFallbackException exception)
            { Fail(new InvalidDataException("Malformed Copilot answer Unicode.", exception)); }
            catch (InvalidDataException exception) { Fail(exception); }
        }
    }

    private void Fail(InvalidDataException exception)
    {
        closed = true;
        snapshots.Writer.TryComplete(exception);
    }

    internal void Complete()
    {
        lock (gate)
        {
            closed = true;
            snapshots.Writer.TryComplete();
        }
    }

    internal async Task PublishAsync(Func<string, CancellationToken, ValueTask> publish, CancellationToken token)
    {
        await foreach (var snapshot in snapshots.Reader.ReadAllAsync(token).ConfigureAwait(false))
        {
            token.ThrowIfCancellationRequested();
            await publish(snapshot, token).ConfigureAwait(false);
        }
    }

    internal void RequireFinal(string? answer)
    {
        lock (gate)
        {
            if (text.Length != 0 && !string.Equals(text.ToString(), answer, StringComparison.Ordinal))
            { throw new InvalidDataException("Copilot final answer differs from its streamed text."); }
        }
    }
}
