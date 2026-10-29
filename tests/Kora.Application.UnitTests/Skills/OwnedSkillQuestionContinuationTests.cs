using AwesomeAssertions;
using Kora.Application.Skills;

namespace Kora.Application.UnitTests.Skills;

public sealed class OwnedSkillQuestionContinuationTests
{
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
