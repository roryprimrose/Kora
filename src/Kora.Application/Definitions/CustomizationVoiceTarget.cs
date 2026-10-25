namespace Kora.Application.Definitions;

/// <summary>One host-published foreground candidate. Retirement is irreversible, including focus ABA.</summary>
public sealed class CustomizationVoiceTarget(CustomizationVoiceContext context, Func<bool> eligible)
{
    private int retired;
    public CustomizationVoiceContext Context { get; } = context;
    public bool IsCurrent => Volatile.Read(ref retired) == 0 && eligible();
    public void Retire() => Interlocked.Exchange(ref retired, 1);
}
