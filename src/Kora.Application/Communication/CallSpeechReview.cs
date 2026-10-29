using System.Diagnostics;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Communication;

// Authority stays inside the host; neither the displayed text nor a response ID can mint a lease.
internal sealed class CallSpeechReview : IDisposable
{
    private readonly Lock gate = new();
    private readonly CallCommunicationPolicy policy;
    private readonly TimeProvider time;
    private readonly DateTimeOffset expires;
    private readonly Func<bool> eligible;
    private readonly ActivityContext reviewContext;
    private readonly CancellationTokenSource expiration;
    private bool consumed;
    private int retired;

    internal CallSpeechReview(CallCommunicationPolicy policy, TimeProvider time, ResponseSpeechTarget target,
        string text, AudioOutputDevice output, Func<bool> eligible)
    {
        this.policy = policy;
        this.time = time;
        this.eligible = eligible;
        Target = target;
        Text = text;
        Output = output;
        var input = RequireLocalInput();
        Request = input.Request;
        reviewContext = input.Activity!.Context;
        expires = time.GetUtcNow().AddMinutes(2);
        if (target.ResponseId == Guid.Empty || string.IsNullOrWhiteSpace(text)
            || string.IsNullOrWhiteSpace(output.Id) || output.IsSystemDefault || output.IsMuted || !Current())
        { throw new InvalidOperationException("Exact call speech review is unavailable or its source/output is not current."); }
        expiration = new(TimeSpan.FromMinutes(2), time);
        Expiration = expiration.Token;
    }

    internal HostRequest Request { get; }
    internal ResponseSpeechTarget Target { get; }
    internal string Text { get; }
    internal AudioOutputDevice Output { get; }
    internal CancellationToken Expiration { get; }
    internal bool Available { get { lock (gate) { return !consumed && Current(); } } }

    internal CallSpeechLease Confirm()
    {
        var input = RequireLocalInput();
        var confirmation = input.Request;
        lock (gate)
        {
            if (confirmation.SessionId != Request.SessionId || confirmation.RequestId == Request.RequestId || !Available)
            { throw new InvalidOperationException("Separate fresh local confirmation of this exact current review is required."); }
            consumed = true;
            input.Activity!.AddLink(new(reviewContext));
            return new(this, input);
        }
    }

    internal bool Current()
    {
        var observation = policy.Current;
        return Volatile.Read(ref retired) == 0 && time.GetUtcNow() < expires
            && Enum.IsDefined(observation.AutomaticState) && observation.IsProtected
            && !observation.ManualControlEvidenceUnavailable
            && observation.Revision == Target.PolicyRevision && eligible();
    }

    internal bool BelongsTo(CallCommunicationPolicy owner) => ReferenceEquals(policy, owner);

    private static HostActivity RequireLocalInput()
    {
        var activity = HostActivity.RequireCurrent();
        if (activity.Outcome != HostOperationOutcome.Unknown
            || activity.Request.Origin != RequestOrigin.LocalUi || activity.Request.InvocationId is not null)
        { throw new InvalidOperationException("Call speech exceptions require original local UI input; voice cannot be relabelled by confirmation."); }
        return activity;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref retired, 1) != 0) { return; }
        expiration.Cancel();
        expiration.Dispose();
    }
}
