using System.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Skills;

/// <summary>Original authority can only be narrowed, never recaptured by a later native confirmation.</summary>
public sealed class OwnedSkillAuthoringAuthority(RequestOrigin origin, Func<bool> eligible)
{
    private int retired;
    public ActivityContext? OriginalActivityContext { get; } = Activity.Current?.Context;
    public RequestOrigin Origin { get; } = origin is RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice
        ? origin : throw new InvalidOperationException("Skill authoring requires original local user input.");
    public bool IsCurrent()
    {
        if (!eligible()) { Retire(); }
        return Volatile.Read(ref retired) == 0;
    }
    public void Retire() => Interlocked.Exchange(ref retired, 1);
}
