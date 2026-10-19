using System.Reflection;

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
    [InlineData(25)]
    [InlineData(100)]
    [InlineData(178)]
    [InlineData(179)]
    [InlineData(200)]
    public void SmootherFrameCadencePreservesElapsedTimeMovement(int speed)
    {
        var smooth = new PresenceParticleField();
        var stepped = new PresenceParticleField();
        for (var index = 0; index < 6; index++)
        {
            smooth.Advance(AssistantState.Information, PresenceAnimation.FrameInterval, 100, speed, false);
        }
        stepped.Advance(AssistantState.Information, PresenceParticleField.QuietFrameInterval, 100, speed, false);
        Project(smooth, 100).Should().Equal(Project(stepped, 100));
    }

    [Theory]
    [InlineData(double.NaN, 360, 1)]
    [InlineData(double.PositiveInfinity, 360, 1)]
    [InlineData(double.NegativeInfinity, 360, 1)]
    [InlineData(-1, 360, 1)]
    [InlineData(360, double.NaN, 1)]
    [InlineData(360, double.PositiveInfinity, 1)]
    [InlineData(360, double.NegativeInfinity, 1)]
    [InlineData(360, -1, 1)]
    [InlineData(360, 0, 1)]
    [InlineData(0, 360, 1)]
    [InlineData(360, 360, double.NaN)]
    [InlineData(360, 360, double.PositiveInfinity)]
    [InlineData(360, 360, double.NegativeInfinity)]
    [InlineData(360, 360, -1)]
    [InlineData(360, 360, 0)]
    public void InvalidGeometryIsRejected(double width, double height, double scale)
    {
        var field = new PresenceParticleField();
        var project = () => field.Project(new PresenceParticle[300], 100, 100, width, height, scale);
        project.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(25)]
    [InlineData(100)]
    [InlineData(200)]
    public void UndersizedDestinationIsRejectedBeforeWritingParticles(int density)
    {
        var field = new PresenceParticleField();
        var sentinel = new PresenceParticle(-1, -2, -3, 4);
        var destination = Enumerable.Repeat(sentinel, PresenceSettings.GetParticleCount(density) - 1).ToArray();

        var project = () => field.Project(destination, density, 100, 360, 360, 1);

        project.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("destination");
        destination.Should().OnlyContain(particle => particle == sentinel);
    }

    [Theory]
    [InlineData(1.17, 0, 0.01, 0, -0.005746, 0)]
    [InlineData(-1.17, 0, -0.01, 0, 0.005746, 0)]
    [InlineData(0, 1.17, 0, 0.01, 0, -0.005746)]
    [InlineData(0, -1.17, 0, -0.01, 0, 0.005746)]
    [InlineData(1.17, -1.17, 0.01, -0.01, -0.005746, 0.005746)]
    public void CrossingAnOuterEdgeReflectsAndDampsOnlyThatVelocity(
        double x, double y, double dx, double dy, double expectedDx, double expectedDy)
    {
        var field = new PresenceParticleField();
        // Prime one overshoot directly rather than depending on a long seeded trajectory reaching an edge.
        var particles = Assert.IsAssignableFrom<Array>(typeof(PresenceParticleField)
            .GetField("particles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(field));
        var particle = particles.GetValue(0)!;
        var particleType = particle.GetType();
        particleType.GetField("X")!.SetValue(particle, x);
        particleType.GetField("Y")!.SetValue(particle, y);
        particleType.GetField("Dx")!.SetValue(particle, dx);
        particleType.GetField("Dy")!.SetValue(particle, dy);

        field.Advance(AssistantState.Executing, PresenceAnimation.FrameInterval, 25, 200, false).Should().BeTrue();

        Assert.IsType<double>(particleType.GetField("X")!.GetValue(particle))
            .Should().BeApproximately(x + dx * 3, 0.000000001);
        Assert.IsType<double>(particleType.GetField("Y")!.GetValue(particle))
            .Should().BeApproximately(y + dy * 3, 0.000000001);
        Assert.IsType<double>(particleType.GetField("Dx")!.GetValue(particle))
            .Should().BeApproximately(expectedDx, 0.000000001);
        Assert.IsType<double>(particleType.GetField("Dy")!.GetValue(particle))
            .Should().BeApproximately(expectedDy, 0.000000001);
    }

    private static PresenceParticle[] Project(PresenceParticleField field, int density)
    {
        var dots = new PresenceParticle[PresenceSettings.GetParticleCount(density)];
        field.Project(dots, density, 100, 360, 360, 1);
        return dots;
    }
}