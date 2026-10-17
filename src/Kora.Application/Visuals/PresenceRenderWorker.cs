using System.Diagnostics;

using Kora.Core;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Visuals;

public sealed partial class PresenceRenderWorker : IDisposable
{
    private const int DiagnosticTransitionLimit = 16;
    private readonly IPresenceRenderTarget target;
    private readonly Action<Action> dispatch;
    private readonly Action<Exception> reportFailure;
    private readonly ILogger<PresenceRenderWorker> logger;
    private readonly Lock gate = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly Thread thread;
    private readonly Func<HostActivity> continuation;
    private Func<HostActivity> failureContext;
    private HostRequest? admittedRequest;
    private PresenceRenderInputs? inputs;
    private long generation;
    private bool enabled;
    private bool pending;
    private bool stopping;
    private bool disposed;
    private bool hasVisibleFrame;
    private bool faulted;
    private DiagnosticState? lastDiagnosticState;
    private int diagnosticTransitions;
    private bool diagnosticsLimited;
    private long drawnFrames;
    private long presentedFrames;

    private readonly record struct DiagnosticState(bool Admitted, long Generation, AssistantState State,
        bool RequestedVisible, bool ReducedMotion, double Width, double Height, double Scaling,
        bool PositiveOpacityPresented);

    public PresenceRenderWorker(IPresenceRenderTarget target, Action<Action> dispatch,
        Action<Exception> reportFailure, ILogger<PresenceRenderWorker> logger)
    {
        this.target = target;
        this.dispatch = dispatch;
        this.reportFailure = reportFailure;
        this.logger = logger;
        continuation = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Presentation);
        failureContext = continuation;
        thread = new Thread(Run) { IsBackground = true, Name = "Kora presence renderer" };
        using (ExecutionContext.SuppressFlow())
        {
            thread.Start();
        }
    }

    public bool HasVisibleFrame
    {
        get
        {
            lock (gate)
            {
                return enabled && !stopping && !disposed && hasVisibleFrame;
            }
        }
    }

    public void Update(PresenceRenderInputs next)
    {
        next.Validate();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (stopping)
            {
                throw new InvalidOperationException("The native presence renderer is unavailable after a rendering failure.");
            }
            var request = HostActivity.Current?.Request;
            if (inputs is null || request is not null && request != admittedRequest
                || inputs.State != next.State || inputs.IsSpeaking != next.IsSpeaking
                || inputs.IsPresenceVisible != next.IsPresenceVisible
                || inputs.Width != next.Width || inputs.Height != next.Height || inputs.Scaling != next.Scaling)
            {
                failureContext = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Presentation);
                admittedRequest = request;
            }
            if (inputs == next && enabled)
            {
                return;
            }
            if (!enabled || inputs is null || inputs.Width != next.Width || inputs.Height != next.Height
                || inputs.Scaling != next.Scaling)
            {
                generation++;
                hasVisibleFrame = false;
            }
            inputs = next;
            enabled = true;
            wake.Set();
        }
    }

    public void Suspend()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            enabled = false;
            hasVisibleFrame = false;
            generation++;
            wake.Set();
        }
        target.Hide();
    }

    private void Run()
    {
        using var activity = continuation();
        try
        {
            var field = new PresenceParticleField();
            var animation = new PresenceAnimation(AssistantState.Information);
            var particles = new PresenceParticle[PresenceSettings.GetParticleCount(PresenceSettings.MaximumDotDensityPercent)];
            PresenceRenderInputs? previous = null;
            long observedGeneration = -1;
            var timestamp = Stopwatch.GetTimestamp();
            var active = false;
            var cadence = PresenceAnimation.FrameInterval;
            while (true)
            {
                PresenceRenderInputs? next;
                long version;
                bool shouldWait;
                bool admitted;
                bool positiveOpacityPresented;
                Func<HostActivity> diagnosticContext;
                lock (gate)
                {
                    if (stopping)
                    {
                        break;
                    }
                    next = inputs;
                    version = generation;
                    shouldWait = !enabled || pending || next is null;
                    admitted = enabled;
                    positiveOpacityPresented = hasVisibleFrame;
                    diagnosticContext = failureContext;
                }
                if (next is not null)
                {
                    RecordDiagnosticTransition(new DiagnosticState(admitted, admitted ? version : 0, next.State,
                        next.IsPresenceVisible, next.ReducedMotion, next.Width, next.Height, next.Scaling,
                        positiveOpacityPresented), diagnosticContext);
                }
                if (shouldWait)
                {
                    wake.WaitOne();
                    lock (gate)
                    {
                        if (!enabled)
                        {
                            timestamp = Stopwatch.GetTimestamp();
                            active = false;
                        }
                    }
                    continue;
                }
                var now = Stopwatch.GetTimestamp();
                var elapsed = Stopwatch.GetElapsedTime(timestamp, now);
                if (active && elapsed < cadence)
                {
                    // Input changes wake promptly; a pending deadline never delays a state or speech edge.
                    if (previous != next)
                    {
                        elapsed = TimeSpan.FromTicks(Math.Min(elapsed.Ticks, cadence.Ticks));
                    }
                    else
                    {
                        wake.WaitOne(cadence - elapsed);
                        continue;
                    }
                }
                timestamp = now;
                if (!active)
                {
                    elapsed = PresenceAnimation.FrameInterval;
                }
                active = true;
                var current = next!;
                if (version != observedGeneration)
                {
                    // Every admission starts a fresh fade, including the first; hidden time is never replayed.
                    animation = new PresenceAnimation(current.State);
                    animation.Advance(AssistantState.Hidden, false, 0, PresenceAnimation.VisibilityTransitionDuration);
                    previous = null;
                    observedGeneration = version;
                    elapsed = TimeSpan.Zero;
                }
                var boundedElapsed = TimeSpan.FromTicks(Math.Min(elapsed.Ticks, PresenceParticleField.QuietFrameInterval.Ticks));
                var inputChanged = previous is not null && previous != current;
                var changed = false;
                if (inputChanged)
                {
                    changed = animation.Advance(previous!.State, previous.IsSpeaking, previous.SpeechOutputLevel,
                        boundedElapsed, previous.SpeechScalingEnabled, previous.SpeechScaleAmount, previous.IsPresenceVisible);
                }
                changed |= animation.Advance(current.State, current.IsSpeaking, current.SpeechOutputLevel,
                    inputChanged ? TimeSpan.Zero : boundedElapsed, current.SpeechScalingEnabled,
                    current.SpeechScaleAmount, current.IsPresenceVisible);
                var visualChanged = changed;
                if (animation.Current.Opacity > 0 && current.State != AssistantState.Hidden)
                {
                    changed |= field.Advance(current.State, boundedElapsed, current.Density, current.MovementSpeed, current.ReducedMotion);
                }
                if (!changed && previous == current)
                {
                    wake.WaitOne();
                    timestamp = Stopwatch.GetTimestamp();
                    active = false;
                    continue;
                }
                previous = current;
                var frame = animation.Current;
                cadence = GetCadence(current, visualChanged, inputChanged);
                var count = field.Project(particles, current.Density, current.DotSize, current.Width, current.Height, frame.Scale);
                var factor = current.IsLightTheme ? 0.55 : 1;
                target.Draw(particles.AsSpan(0, count), current.Width, current.Height, current.Scaling,
                    (byte)Math.Round(frame.Color.Red * factor), (byte)Math.Round(frame.Color.Green * factor),
                    (byte)Math.Round(frame.Color.Blue * factor), frame.Opacity);
                lock (gate)
                {
                    drawnFrames++;
                    if (!enabled || stopping || generation != version)
                    {
                        continue;
                    }
                    pending = true;
                }
                dispatch(() => Present(version, frame.Opacity > 0));
            }
            activity.Complete(faulted ? HostOperationOutcome.Failed : HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Fail(exception);
            activity.Complete(HostOperationOutcome.Failed);
        }
        finally
        {
            try
            {
                target.ReleaseDrawingResources();
                RecordDiagnosticCompletion();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Fail(exception);
            }
        }
    }

    internal static TimeSpan GetCadence(PresenceRenderInputs current, bool visualChanged, bool inputChanged) =>
        current.IsSpeaking || visualChanged || inputChanged
            || PresenceParticleField.GetActivity(current.State, current.MovementSpeed, !current.ReducedMotion) > 0
            ? PresenceAnimation.FrameInterval : PresenceParticleField.QuietFrameInterval;

    private void Present(long version, bool visible)
    {
        try
        {
            bool admit;
            lock (gate)
            {
                admit = !stopping && !disposed && enabled && generation == version;
            }
            if (admit)
            {
                target.Present();
                lock (gate)
                {
                    hasVisibleFrame = visible;
                    presentedFrames++;
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Fail(exception);
        }
        finally
        {
            lock (gate)
            {
                pending = false;
                if (!disposed)
                {
                    wake.Set();
                }
            }
        }
    }

    private void RecordDiagnosticTransition(DiagnosticState state, Func<HostActivity> context)
    {
        if (diagnosticsLimited || !logger.IsEnabled(LogLevel.Information) || lastDiagnosticState == state)
        {
            return;
        }
        using var activity = context();
        if (diagnosticTransitions == DiagnosticTransitionLimit)
        {
            diagnosticsLimited = true;
            RenderDiagnosticLimitReached(DiagnosticTransitionLimit);
        }
        else
        {
            lastDiagnosticState = state;
            diagnosticTransitions++;
            RenderTransition(diagnosticTransitions, state.Admitted, state.Generation, state.State,
                state.RequestedVisible, state.ReducedMotion, state.Width, state.Height, state.Scaling,
                state.PositiveOpacityPresented);
        }
        activity.Complete(HostOperationOutcome.Completed);
    }

    private void RecordDiagnosticCompletion()
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }
        Func<HostActivity> context;
        long drawn;
        long presented;
        lock (gate)
        {
            context = failureContext;
            drawn = drawnFrames;
            presented = presentedFrames;
        }
        using var activity = context();
        RenderCompleted(drawn, presented, diagnosticTransitions, diagnosticsLimited);
        activity.Complete(HostOperationOutcome.Completed);
    }

    private void Fail(Exception exception)
    {
        Func<HostActivity> cause;
        lock (gate)
        {
            stopping = true;
            faulted = true;
            hasVisibleFrame = false;
            enabled = false;
            generation++;
            cause = failureContext;
            wake.Set();
        }
        using var activity = cause();
        RenderFailed(exception);
        activity.Complete(HostOperationOutcome.Failed);
        dispatch(() => reportFailure(exception));
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            stopping = true;
            enabled = false;
            generation++;
            wake.Set();
        }
        target.Hide();
        // The worker never invokes or waits for the UI. No queued presentation must run to release its resources.
#pragma warning disable VSTHRD002
        if (!thread.Join(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("The native presence drawing worker did not release its resources.");
        }
#pragma warning restore VSTHRD002
        lock (gate)
        {
            disposed = true;
            wake.Dispose();
        }
    }
}