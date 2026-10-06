using Avalonia.Media;
using AwesomeAssertions;
using Kora.Setup;

namespace Kora.Windows.IntegrationTests;

public sealed class AnimatedMarkTests
{
    [Fact]
    public void AnimationRequestsEveryFollowingFrameWithoutExternalInvalidation()
    {
        var frames = new RecordingFrames();
        var phases = new List<double>();
        var animation = new AnimatedMark.AnimationLoop(frames.Request, phases.Add);

        animation.Start();
        animation.Start();
        frames.PendingCount.Should().Be(1);

        for (var frame = 0; frame <= 480; frame++)
        {
            frames.Pulse(TimeSpan.FromSeconds(100 + (frame / 60.0)));
            frames.PendingCount.Should().Be(1);
        }

        phases.Should().HaveCount(481);
        phases[0].Should().Be(0);
        phases[120].Should().BeApproximately(0.25, 0.000001);
        phases[240].Should().BeApproximately(0.5, 0.000001);
        phases[360].Should().BeApproximately(0.75, 0.000001);
        phases[480].Should().Be(0);
        GC.KeepAlive(animation);
    }

    [Fact]
    public void PhaseUsesElapsedFrameTimeInsteadOfCountingTicks()
    {
        var frames = new RecordingFrames();
        var phases = new List<double>();
        var animation = new AnimatedMark.AnimationLoop(frames.Request, phases.Add);
        animation.Start();

        frames.Pulse(TimeSpan.FromSeconds(20));
        frames.Pulse(TimeSpan.FromSeconds(22));
        frames.Pulse(TimeSpan.FromSeconds(29));

        phases.Should().Equal(0, 0.25, 0.125);
        GC.KeepAlive(animation);
    }

    [Fact]
    public void ReducedMotionAndDetachmentStopTheLoopAndRestoreStaticColours()
    {
        var frames = new RecordingFrames();
        var phases = new List<double>();
        var animation = new AnimatedMark.AnimationLoop(frames.Request, phases.Add);
        animation.Start();
        frames.Pulse(TimeSpan.FromSeconds(10));
        frames.Pulse(TimeSpan.FromSeconds(12));

        animation.Stop();
        animation.Stop();
        animation.IsRunning.Should().BeFalse();
        phases.Should().Equal(0, 0.25, 0);

        frames.Pulse(TimeSpan.FromSeconds(14));
        phases.Should().Equal(0, 0.25, 0);
        frames.PendingCount.Should().Be(0);
    }

    [Fact]
    public void DisabledAnimationDoesNotRequestFrames()
    {
        var frames = new RecordingFrames();
        var phases = new List<double>();
        var animation = new AnimatedMark.AnimationLoop(frames.Request, phases.Add);

        animation.Stop();

        animation.IsRunning.Should().BeFalse();
        frames.PendingCount.Should().Be(0);
        phases.Should().BeEmpty();
    }

    [Fact]
    public void ObsoletePendingFrameCannotRestartAnimationAfterReattachment()
    {
        var frames = new RecordingFrames();
        var phases = new List<double>();
        var animation = new AnimatedMark.AnimationLoop(frames.Request, phases.Add);
        animation.Start();
        animation.Stop();
        animation.Start();
        frames.PendingCount.Should().Be(2);

        frames.Pulse(TimeSpan.FromSeconds(10));
        phases.Should().Equal(0);
        frames.PendingCount.Should().Be(1);

        frames.Pulse(TimeSpan.FromSeconds(20));
        frames.Pulse(TimeSpan.FromSeconds(22));
        phases.Should().Equal(0, 0, 0.25);
        frames.PendingCount.Should().Be(1);
        GC.KeepAlive(animation);
    }

    [Fact]
    public void StoppingFromFrameCallbackDoesNotQueueAnotherFrame()
    {
        var frames = new RecordingFrames();
        AnimatedMark.AnimationLoop? animation = null;
        animation = new AnimatedMark.AnimationLoop(frames.Request, _ => animation!.Stop());
        animation.Start();

        frames.Pulse(TimeSpan.Zero);

        animation.IsRunning.Should().BeFalse();
        frames.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData(0, "#F49C9C")]
    [InlineData(1, "#F0CC83")]
    [InlineData(2, "#9BDFAC")]
    [InlineData(3, "#6AE1DA")]
    [InlineData(4, "#B8D9EC")]
    [InlineData(5, "#80B7FF")]
    [InlineData(6, "#AF9BFF")]
    public void AllSevenColoursTravelAroundTheRibbonAndRepeatAfterEightSeconds(int index, string colour)
    {
        var frames = new RecordingFrames();
        var phases = new List<double>();
        var animation = new AnimatedMark.AnimationLoop(frames.Request, phases.Add);
        var position = index / 7.0;
        var expected = Color.Parse(colour);
        animation.Start();

        frames.Pulse(TimeSpan.FromSeconds(100));
        AnimatedMark.ColourAt(position - phases[^1]).Should().Be(expected);
        frames.Pulse(TimeSpan.FromSeconds(104));
        AnimatedMark.ColourAt(position + 0.5 - phases[^1]).Should().Be(expected);
        AnimatedMark.ColourAt(position - phases[^1]).Should().NotBe(expected);
        frames.Pulse(TimeSpan.FromSeconds(108));
        AnimatedMark.ColourAt(position - phases[^1]).Should().Be(expected);
        AnimatedMark.ColourAt(position - 1).Should().Be(expected);
        GC.KeepAlive(animation);
    }

    [Fact]
    public void ColoursInterpolateContinuouslyAcrossTheLoopBoundary()
    {
        AnimatedMark.ColourAt(0.5 / 7).Should().Be(Color.FromRgb(242, 180, 144));
        AnimatedMark.ColourAt(6.5 / 7).Should().Be(Color.FromRgb(210, 156, 206));
        AnimatedMark.ColourAt(-0.5 / 7).Should().Be(AnimatedMark.ColourAt(6.5 / 7));
        AnimatedMark.ColourAt(1).Should().Be(AnimatedMark.ColourAt(0));
    }

    private sealed class RecordingFrames
    {
        private readonly Queue<Action<TimeSpan>> pending = new();

        public int PendingCount => pending.Count;

        public void Request(Action<TimeSpan> callback) => pending.Enqueue(callback);

        public void Pulse(TimeSpan timestamp) => pending.Dequeue()(timestamp);
    }
}
