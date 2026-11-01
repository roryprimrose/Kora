using Kora.Application.Skills;
using Kora.Core.Hosting;
using Kora.Core.Skills;

namespace Kora;

internal sealed partial class OwnedSkillEditorController
{
    private CancellationTokenSource? fixtureLifetime;
    private OwnedSkillAuthoringAuthority? fixtureAuthority;
    private long fixtureEpoch;

    private void CancelFixture()
    {
        Interlocked.Increment(ref fixtureEpoch);
        fixtureLifetime?.Cancel();
        if (fixtureLifetime is not null) { window?.RetireReview(); }
    }

    private async Task TestFixture(OwnedSkillEditorWindow opened, OwnedSkillAuthoringAuthority request, CancellationToken token)
    {
        CancelFixture();
        if (fixtures is null || uncertain || fixtureAuthority is not { } input)
        { throw new InvalidOperationException("Simulated testing is unavailable, publication is uncertain, or an explicit fixture is required."); }
        var draft = pendingRefinement is { } proposal
            ? proposal.IsCurrent() ? proposal.Proposed : throw new InvalidOperationException("The guidance proposal expired.")
            : opened.Document();
        var fixture = OwnedSkillFixture.Parse(opened.Fixture.Text ?? "");
        var exact = Interlocked.Read(ref generation);
        var epoch = Interlocked.Read(ref fixtureEpoch);
        var originalSource = source;
        var proposalEpoch = Interlocked.Read(ref refinementEpoch);
        var host = main.CaptureOwnedSkillFixtureEligibility();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        fixtureLifetime = cancellation;
        var authority = new OwnedSkillAuthoringAuthority(input.Origin, () => Eligible() && request.IsCurrent() && input.IsCurrent());
        bool Current() => host() && Eligible() && authority.IsCurrent() && !cancellation.IsCancellationRequested
            && exact == Interlocked.Read(ref generation) && epoch == Interlocked.Read(ref fixtureEpoch)
            && proposalEpoch == Interlocked.Read(ref refinementEpoch) && ReferenceEquals(source, originalSource);
        Func<bool> runCurrent = static () => true;
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => { if (!Current() || !runCurrent()) { opened.RetireReview(); cancellation.Cancel(); } };
        timer.Start();
        try
        {
            var shown = await fixtures.Run(draft, originalSource, fixture, authority, Current,
                (preview, admitted) =>
                {
                    runCurrent = preview.IsCurrent;
                    return opened.ReviewFixture(preview, () => Current() && preview.IsCurrent(), admitted);
                },
                (result, admitted) => opened.ShowFixtureResult(result, () => Current() && result.Preview.IsCurrent(), admitted), cancellation.Token);
            opened.Report(shown
                ? "SIMULATED example finished and joined. Structure/example behavior only, not harmlessness or qualification. Nothing saved, enabled, retained as facts or invoked as a real task."
                : "SIMULATED test not started. No model or tools invoked; nothing saved or enabled.");
        }
        finally { timer.Stop(); fixtureLifetime = null; }
    }
}
