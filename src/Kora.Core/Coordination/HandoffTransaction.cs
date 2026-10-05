using System.Security.Cryptography;

namespace Kora.Core.Coordination;

/// <summary>A process-bound, in-memory, single-use lifecycle transaction; never a tool grant.</summary>
public sealed class HandoffTransaction
{
    private readonly byte[] secret = RandomNumberGenerator.GetBytes(32);
    private readonly Lock sync = new();
    private HandoffStage stage;

    public HandoffTransaction(
        Guid ownerEpoch,
        InstanceProcessIdentity original,
        InstanceProcessIdentity candidate,
        DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(candidate);
        if (ownerEpoch == Guid.Empty)
        {
            throw new ArgumentException("A live owner epoch is required.", nameof(ownerEpoch));
        }

        if (!original.Build.HasValidContentDigest || string.IsNullOrWhiteSpace(original.UserSid) ||
            original.SessionId <= 0 || original.ProcessId <= 0 || original.CreationTime <= 0)
        {
            throw new ArgumentException("The original requires a proven process/content identity.", nameof(original));
        }

        if (!candidate.Build.HasValidContentDigest ||
            !string.Equals(original.UserSid, candidate.UserSid, StringComparison.Ordinal) ||
            original.SessionId != candidate.SessionId ||
            original.ProcessId == candidate.ProcessId ||
            candidate.ProcessId <= 0 || candidate.CreationTime <= 0 ||
            original.Build.IsSameBuild(candidate.Build))
        {
            throw new ArgumentException("A handoff requires a proven distinct build/process in the same user/session.", nameof(candidate));
        }

        OwnerEpoch = ownerEpoch;
        Original = original;
        Candidate = candidate;
        ExpiresAt = expiresAt;
        Id = Guid.NewGuid();
    }

    public Guid Id { get; }

    public Guid OwnerEpoch { get; }

    public InstanceProcessIdentity Original { get; }

    public InstanceProcessIdentity Candidate { get; }

    public DateTimeOffset ExpiresAt { get; }

    public HandoffStage Stage
    {
        get
        {
            lock (sync)
            {
                return stage;
            }
        }
    }

    public bool Approve(DateTimeOffset now, bool eligible, bool candidateAlive) =>
        Advance(HandoffStage.Proposed, HandoffStage.Approved, now, eligible && candidateAlive);

    public bool MarkQuiescent(DateTimeOffset now, bool hostDisposed, bool candidateAlive) =>
        Advance(HandoffStage.Approved, HandoffStage.Quiescent, now, hostDisposed && candidateAlive);

    public string? IssueTicket(DateTimeOffset now)
    {
        lock (sync)
        {
            return stage == HandoffStage.Quiescent && now < ExpiresAt
                ? Convert.ToHexString(secret)
                : null;
        }
    }

    public bool Commit(
        Guid epoch,
        Guid transactionId,
        string ticket,
        InstanceProcessIdentity candidate,
        DateTimeOffset now,
        bool eligible)
    {
        lock (sync)
        {
            if (stage != HandoffStage.Quiescent || now >= ExpiresAt || !eligible ||
                epoch != OwnerEpoch || transactionId != Id || candidate != Candidate ||
                ticket.Length != secret.Length * 2)
            {
                return false;
            }

            byte[] supplied;
            try
            {
                supplied = Convert.FromHexString(ticket);
            }
            catch (FormatException)
            {
                return false;
            }
            if (!CryptographicOperations.FixedTimeEquals(secret, supplied))
            {
                return false;
            }

            stage = HandoffStage.Transferred;
            CryptographicOperations.ZeroMemory(secret);
            return true;
        }
    }

    public void Cancel()
    {
        lock (sync)
        {
            if (stage != HandoffStage.Transferred)
            {
                stage = HandoffStage.Cancelled;
                CryptographicOperations.ZeroMemory(secret);
            }
        }
    }

    private bool Advance(HandoffStage expected, HandoffStage next, DateTimeOffset now, bool allowed)
    {
        lock (sync)
        {
            if (stage != expected || now >= ExpiresAt || !allowed)
            {
                return false;
            }

            stage = next;
            return true;
        }
    }
}
