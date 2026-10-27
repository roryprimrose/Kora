using Kora.Core.Hosting;

namespace Kora.Application.Definitions;

/// <summary>Volatile original editor input; only its exact clarification may hand over capture.</summary>
public sealed class CustomizationVoiceRequest(CustomizationVoiceTarget target, string text, HostRequest request,
    Func<bool> admitted, Func<long> generation)
{
    private long expectedGeneration = generation();
    private int retired;
    private int handingOver;
    public CustomizationVoiceTarget Target { get; } = target;
    public string Text { get; } = text;
    public HostRequest Request { get; } = request.Origin is RequestOrigin.ActivatedVoice ? request
        : throw new ArgumentException("Editor voice continuation requires the original activated voice channel.", nameof(request));
    public bool IsAdmitted => Volatile.Read(ref retired) == 0 && Target.IsCurrent && admitted();
    public bool IsCurrent => IsAdmitted && Volatile.Read(ref handingOver) == 0
        && Interlocked.Read(ref expectedGeneration) == generation();

    internal bool BeginHandover()
    {
        if (!IsCurrent) { return false; }
        return Interlocked.CompareExchange(ref handingOver, 1, 0) == 0;
    }

    internal void CompleteHandover(long releasedGeneration)
    {
        if (!IsAdmitted || releasedGeneration != generation())
        { Retire(); return; }
        Interlocked.Exchange(ref expectedGeneration, releasedGeneration);
        Volatile.Write(ref handingOver, 0);
    }

    public void Retire() => Interlocked.Exchange(ref retired, 1);
}
