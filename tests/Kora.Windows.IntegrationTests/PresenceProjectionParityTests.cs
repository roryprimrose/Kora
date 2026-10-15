using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Application.Visuals;
using Kora.Controls;
using Kora.Core;
using Kora.Core.Presentation;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class PresenceProjectionParityTests
{
    private static readonly int[] Densities = [25, 100, 200];
    [Theory]
    [InlineData(AssistantState.Listening)]
    [InlineData(AssistantState.Calculating)]
    [InlineData(AssistantState.Waiting)]
    [InlineData(AssistantState.Executing)]
    [InlineData(AssistantState.Success)]
    [InlineData(AssistantState.Failure)]
    [InlineData(AssistantState.Information)]
    public async Task NativeFieldPreservesPr250SeededMotionAndProjection(AssistantState state)
    {
        await HeadlessSession.RunAsync(() =>
        {
            foreach (var density in Densities)
            {
                var scheduler = new Scheduler();
                using var control = new PresenceControl(scheduler)
                {
                    IsPresentationEligible = true,
                    State = state,
                    DotDensityPercent = density,
                    DotSizePercent = 200,
                    MovementSpeedPercent = 200,
                };
                var field = new PresenceParticleField();
                var window = new Window { Width = 360, Height = 360, Content = control };
                window.Show();
                window.UpdateLayout();
                try
                {
                    scheduler.Advance(PresenceParticleField.QuietFrameInterval);
                    field.Advance(state, PresenceParticleField.QuietFrameInterval, density, 200, false);
                    var projected = new PresenceParticle[300];
                    var count = field.Project(projected, density, 200, 360, 360, 1);
                    var selected = control.DepthOrder.ToArray().Where(index => index < count).ToArray();
                    for (var ordinal = 0; ordinal < selected.Length; ordinal++)
                    {
                        var particle = control.Particles[selected[ordinal]];
                        var perspective = 0.72 + (particle.Z + 1) * 0.18;
                        projected[ordinal].X.Should().BeApproximately(180 + particle.X * 360 * 0.36 * perspective, 0.00000001);
                        projected[ordinal].Y.Should().BeApproximately(180 + particle.Y * 360 * 0.36 * perspective, 0.00000001);
                        projected[ordinal].Radius.Should().BeApproximately(
                            particle.Size * 2 * (0.7 + (particle.Z + 1) * 0.24), 0.00000001);
                    }
                }
                finally
                {
                    window.Close();
                }
            }
        });
    }

    private sealed class Scheduler : IPresenceFrameScheduler
    {
        private readonly List<Request> requests = [];
        public TimeSpan Now { get; private set; }
        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            var request = new Request(Now + delay, callback);
            requests.Add(request);
            return request;
        }
        public void Advance(TimeSpan duration)
        {
            var end = Now + duration;
            while (requests.FirstOrDefault(request => !request.Cancelled && request.Due <= end) is { } next)
            {
                Now = next.Due;
                next.Dispose();
                next.Callback();
            }
            Now = end;
        }
        private sealed class Request(TimeSpan due, Action callback) : IDisposable
        {
            public TimeSpan Due { get; } = due;
            public Action Callback { get; } = callback;
            public bool Cancelled { get; private set; }
            public void Dispose() => Cancelled = true;
        }
    }
}