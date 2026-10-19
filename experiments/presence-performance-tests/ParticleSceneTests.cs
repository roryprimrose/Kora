using AwesomeAssertions;

using Kora.Core;

namespace Presence.Performance.Tests;

public sealed class ParticleSceneTests
{
    [Theory]
    [InlineData(38, false)]
    [InlineData(150, false)]
    [InlineData(300, false)]
    [InlineData(38, true)]
    [InlineData(300, true)]
    public void ScenePreservesCountAndBounds(int count, bool cached)
    {
        using var scene = new ParticleScene(count, cached);
        scene.Count.Should().Be(count);
        scene.CacheBytes.Should().Be(cached ? 600 * count * 2 * sizeof(float) : 0);
        for (var index = 0; index < count; index++)
        {
            var dot = scene.GetDot(index, 600, 200);
            double.IsFinite(dot.X).Should().BeTrue();
            double.IsFinite(dot.Y).Should().BeTrue();
            dot.Radius.Should().BeInRange(0.98, 4.5);
            dot.Depth.Should().BeInRange(0, 5);
        }
    }

    [Fact]
    public void DensityPreservesTheSeededSubsetRatherThanSelectingByDepth()
    {
        using var small = new ParticleScene(38, false);
        using var full = new ParticleScene(300, false);
        var fullDots = Enumerable.Range(0, 300).Select(index => full.GetDot(index, 360, 100)).ToArray();
        foreach (var dot in Enumerable.Range(0, 38).Select(index => small.GetDot(index, 360, 100)))
        {
            fullDots.Should().Contain(dot);
        }
        small.GetDot(37, 360, 100).Depth.Should().BeGreaterThan(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailureSettlesAndStopsChanging(bool cached)
    {
        using var scene = new ParticleScene(150, cached);
        scene.Advance(AssistantState.Failure, false, 0, TimeSpan.FromSeconds(2), 200, true);
        scene.Advance(AssistantState.Failure, false, 0, TimeSpan.FromSeconds(2), 200, true).Should().BeFalse();
        var dots = Enumerable.Range(0, 150).Select(index => scene.GetDot(index, 360, 100)).ToArray();
        scene.Advance(AssistantState.Failure, false, 0, TimeSpan.FromSeconds(2), 200, true);
        Enumerable.Range(0, 150).Select(index => scene.GetDot(index, 360, 100)).Should().Equal(dots);
    }

    [Theory]
    [InlineData(0, 0.8)]
    [InlineData(1, 1.24)]
    public void SpeechChangesSpreadWithoutChangingDotRadius(double level, double expectedScale)
    {
        using var scene = new ParticleScene(150, false);
        var before = scene.GetDot(0, 600, 200);
        scene.Advance(AssistantState.Failure, true, level, TimeSpan.FromSeconds(2), 100, false);
        var after = scene.GetDot(0, 600, 200);
        scene.Frame.Scale.Should().BeApproximately(expectedScale, 0.000001);
        after.Radius.Should().Be(before.Radius);
        (after.X - 300).Should().BeApproximately((before.X - 300) * expectedScale, 0.000001);
        (after.Y - 300).Should().BeApproximately((before.Y - 300) * expectedScale, 0.000001);
    }

    [Theory]
    [InlineData(-1, -1, -1)]
    [InlineData(0.4, -0.7, 0.8)]
    [InlineData(1, 1, 1)]
    public void CachedTrajectoriesJoinWithoutAPositionJump(double x, double y, double z)
    {
        var start = ParticleScene.ClosedPosition(x, y, z, 0);
        var end = ParticleScene.ClosedPosition(x, y, z, 1);
        start.X.Should().BeApproximately(end.X, 0.00000000001);
        start.Y.Should().BeApproximately(end.Y, 0.00000000001);
    }
}