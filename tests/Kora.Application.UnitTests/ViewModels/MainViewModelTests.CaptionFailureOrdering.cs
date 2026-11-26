using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.ViewModels;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private sealed class CaptionFailureAuditGates : IDisposable, ILogger<MainViewModel>
    {
        private readonly ManualResetEventSlim grantRelease = new();
        private readonly ManualResetEventSlim captionRelease = new();
        private readonly TaskCompletionSource grantEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource captionEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int requestedGrants;

        public void BeforeWrite(SecurityAuditEvent audit)
        {
            if (audit.Category == SecurityAuditCategory.SecurityApproval
                && audit.Outcome == SecurityAuditOutcome.Requested
                && Interlocked.Increment(ref requestedGrants) == 1)
            {
                grantEntered.SetResult();
                grantRelease.Wait(TestContext.Current.CancellationToken);
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // Pause outside the preference store's lock, after its failed durable receipt.
            if (logLevel == LogLevel.Error)
            {
                captionEntered.SetResult();
                captionRelease.Wait(TestContext.Current.CancellationToken);
            }
        }

        public Task WaitUntilPreparationAndFailureEntered() =>
            Task.WhenAll(grantEntered.Task, captionEntered.Task).WaitAsync(TestContext.Current.CancellationToken);

        public void ReleaseGrant() => grantRelease.Set();
        public void ReleaseCaptionFailure() => captionRelease.Set();
        public void ReleaseAll() { ReleaseGrant(); ReleaseCaptionFailure(); }
        public void Dispose() { grantRelease.Dispose(); captionRelease.Dispose(); }
    }
}
