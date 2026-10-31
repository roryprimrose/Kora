using Kora.Windows.IntegrationTests;

using Xunit;

using NativeFixturePhaseOutcome = Kora.NativeUxFixture.NativeFixturePhaseScope.Outcome;

namespace Kora.NativeUxFixture;

internal sealed partial class NativeUxFixtureSession
{
    private NativeFixturePhaseTimings? phaseTimings;

    partial void CapturePhaseObserver() =>
        phaseTimings = NativeFixturePhaseTimings.Capture(TestContext.Current);

    partial void ObservePhase(NativeFixturePhaseScope phase, NativeFixturePhaseOutcome outcome) =>
        (phaseTimings ?? throw new InvalidOperationException("Native phase evidence has no test owner.")).Observe(phase, outcome);

    partial void VerifyPhaseEvidence() =>
        (phaseTimings ?? throw new InvalidOperationException("Native phase evidence has no test owner.")).ThrowIfPublicationFailed();
}
