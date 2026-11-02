using AwesomeAssertions;
using Kora.Application.Skills;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Skills;

public sealed class OwnedSkillQuestionContinuationTests
{
    [Fact]
    public void AcceptedMultiFieldCaptureContinuationCannotRenewRetiredOriginalAuthority()
    {
        var generation = 1L;
        var stable = true;
        var continuation = new OwnedSkillQuestionContinuation(() => generation == 1, () => stable, () => generation);
        var authority = new OwnedSkillAuthoringAuthority(RequestOrigin.ActivatedVoice, continuation.IsCurrent)
        { Questions = continuation };
        using (continuation.Open())
        {
            generation++;
            authority.IsCurrent().Should().BeTrue();
            generation++;
            authority.IsCurrent().Should().BeTrue();
        }
        authority.IsCurrent().Should().BeTrue("the accepted field captures remain part of the original admitted collection");
        generation++;
        authority.IsCurrent().Should().BeFalse("an unrelated later capture cannot reuse the completed collection");
        generation--;
        stable = false;
        authority.IsCurrent().Should().BeFalse();
        stable = true;
        authority.IsCurrent().Should().BeFalse("restoring an old generation or privacy state cannot revive retired authority");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyAnAdmittedCollectionCanAdvanceCaptureGenerationWhileStablePrivacyRemainsMandatory(bool revoke)
    {
        var generation = 1L;
        var stable = true;
        var continuation = new OwnedSkillQuestionContinuation(() => generation == 1, () => stable, () => generation);
        continuation.IsCurrent().Should().BeTrue();
        generation++;
        continuation.IsCurrent().Should().BeFalse();
        continuation.Invoking(value => value.Open()).Should().Throw<InvalidOperationException>();
        generation = 1;
        using var collection = continuation.Open();
        continuation.Invoking(value => value.Open()).Should().Throw<InvalidOperationException>();
        generation++;
        continuation.IsCurrent().Should().BeTrue();
        if (revoke) { stable = false; continuation.IsCurrent().Should().BeFalse(); }
        collection.Dispose();
        continuation.IsCurrent().Should().Be(!revoke);
        generation++;
        collection.Dispose();
        continuation.IsCurrent().Should().BeFalse("disposal cannot recapture a later unrelated activation");
    }
}
