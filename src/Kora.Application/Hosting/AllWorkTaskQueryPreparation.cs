namespace Kora.Application.Hosting;

/// <summary>Exact retained task correlation and revocable original-input admission; no task authority.</summary>
public sealed class AllWorkTaskQueryPreparation(Guid taskId, Func<bool> isAdmitted)
{
    private Func<bool>? nativeCurrent;

    public Guid TaskId { get; } = taskId;
    public bool IsAdmitted() => isAdmitted();
    public bool IsPrepared => nativeCurrent is not null && isAdmitted() && nativeCurrent();
    public void Acknowledge(Func<bool> current) => nativeCurrent = current;
    public override string ToString() => nameof(AllWorkTaskQueryPreparation);
}
