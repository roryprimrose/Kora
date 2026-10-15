using AwesomeAssertions;

using Kora.Application.Visuals;
using Kora.Core;
using Kora.Core.Configuration;
using Kora.Core.Presentation;

namespace Kora.Application.UnitTests.Visuals;

public sealed class PresenceParticleFieldTests
{
    [Theory]
    [InlineData(25, 38)]
    [InlineData(100, 150)]
    [InlineData(200, 300)]
    public void DensityPreservesSeededSubsetAndDepthOrder(int density, int expected)
    {
        var field = new PresenceParticleField();
        var selected = Project(field, density);
        var all = Project(field, 200);
        selected.Should().HaveCount(expected);
        selected.Select(dot => dot.Depth).Should().BeInAscendingOrder();
        foreach (var particle in selected)
        {
            all.Should().Contain(particle);
        }
        selected.Last().Depth.Should().BeGreaterThan(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReducedMotionAndFailurePreserveCurrentArrangement(bool reducedMotion)
    {
        var field = new PresenceParticleField();
        field.Advance(AssistantState.Calculating, TimeSpan.FromMilliseconds(33), 100, 100, false);
        var before = Project(field, 100);
        field.Advance(reducedMotion ? AssistantState.Executing : AssistantState.Failure,
            TimeSpan.FromSeconds(2), 100, 200, reducedMotion).Should().BeFalse();
        Project(field, 100).Should().Equal(before);
    }

    [Theory]
    [InlineData(0.8)]
    [InlineData(1.24)]
    public void SpeechScalesSpreadNotRadius(double scale)
    {
        var field = new PresenceParticleField();
        var resting = Project(field, 100);
        var scaled = new PresenceParticle[resting.Length];
        field.Project(scaled, 100, 100, 360, 360, scale);
        for (var index = 0; index < resting.Length; index++)
        {
            scaled[index].Radius.Should().Be(resting[index].Radius);
            scaled[index].X.Should().BeApproximately(180 + (resting[index].X - 180) * scale, 0.000001);
            scaled[index].Y.Should().BeApproximately(180 + (resting[index].Y - 180) * scale, 0.000001);
        }
    }

    [Fact]
    public void ElapsedTimeScalesMovementAndBoundsLargeDelays()
    {
        var regular = new PresenceParticleField();
        var delayed = new PresenceParticleField();
        regular.Advance(AssistantState.Waiting, PresenceParticleField.QuietFrameInterval, 100, 200, false);
        delayed.Advance(AssistantState.Waiting, TimeSpan.FromHours(1), 100, 200, false);
        Project(regular, 100).Should().Equal(Project(delayed, 100));
    }

    [Theory]
    [InlineData(double.NaN, 360, 1)]
    [InlineData(360, double.PositiveInfinity, 1)]
    [InlineData(0, 360, 1)]
    [InlineData(360, 360, 0)]
    public void InvalidGeometryIsRejected(double width, double height, double scale)
    {
        var field = new PresenceParticleField();
        var project = () => field.Project(new PresenceParticle[300], 100, 100, width, height, scale);
        project.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static PresenceParticle[] Project(PresenceParticleField field, int density)
    {
        var dots = new PresenceParticle[PresenceSettings.GetParticleCount(density)];
        field.Project(dots, density, 100, 360, 360, 1);
        return dots;
    }
}