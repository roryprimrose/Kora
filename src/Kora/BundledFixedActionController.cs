using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Threading;

using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Application.Skills;
using Kora.Application.ViewModels;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Skills;
using Kora.Definitions.Skills;
using Kora.Tools.SessionControl;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class BundledFixedActionController : IDisposable
{
    private readonly MainViewModel main;
    private readonly SkillPackagesWindowController sources;
    private readonly NativeQuestionHost questions;
    private readonly ApplicationLifecycleController lifecycle;
    private readonly WindowsSqliteHostInteractionStore store;
    private readonly HostTaskCoordinator tasks;
    private readonly HostAuthorizationService authorization;
    private readonly IBundledScriptWorker worker;
    private readonly FixedSessionActionBroker broker;
    private readonly NativeDetailRenderer renderer;
    private readonly Func<Window> owner;
    private readonly Func<bool> ownsHost;
    private readonly Func<long> ownershipRevision;
    private readonly ILogger<BundledFixedActionController> logger;
    private Task active = Task.CompletedTask;
    private CancellationTokenSource? lifetime;
    private QuestionWindow? questionWindow;
    private bool disposed;
    private WorkSessionAuthorization? reviewedControlSession;
#pragma warning disable VSTHRD003 // Exposes the owned operation for lifecycle observation without starting another invocation.
    internal Task ActiveOperation => active;
#pragma warning restore VSTHRD003
    internal string Status { get; private set; } = "No bundled invocation is pending. No computer-power request has been sent.";

    internal BundledFixedActionController(MainViewModel main, SkillPackagesWindowController sources,
        NativeQuestionHost questions, ApplicationLifecycleController lifecycle, WindowsSqliteHostInteractionStore store,
        HostTaskCoordinator tasks, HostAuthorizationService authorization, IBundledScriptWorker worker,
        FixedSessionActionBroker broker, NativeDetailRenderer renderer, Func<Window> owner,
        Func<bool> ownsHost, Func<long> ownershipRevision, ILogger<BundledFixedActionController> logger)
    {
        this.main = main;
        this.sources = sources;
        this.questions = questions;
        this.lifecycle = lifecycle;
        this.store = store;
        this.tasks = tasks;
        this.authorization = authorization;
        this.worker = worker;
        this.broker = broker;
        this.renderer = renderer;
        this.owner = owner;
        this.ownsHost = ownsHost;
        this.ownershipRevision = ownershipRevision;
        this.logger = logger;
        main.BindBundledActions(RequestAsync, JoinAsync, () => Status);
        main.PrivacyClosureRequested += OnPrivacyClosed;
        sources.BindBundledExecution(StartSelected, StartAddressed);
    }

    private void StartSelected(SkillPackageSnapshot package)
    {
        if (!active.IsCompleted)
        { main.ReportHostInteractionFailure("Wait for or cancel the current bundled operation before starting another."); return; }
        active = ExecuteAsync(package.Manifest.Id, RequestOrigin.LocalUi, () => sources.IsReviewing(package));
    }

    private void StartAddressed(SkillPackageSnapshot package)
    {
        if (!active.IsCompleted || reviewedControlSession is not { } target)
        { main.ReportHostInteractionFailure("No idle, previously reviewed control session is available. Nothing was invoked."); return; }
        active = ExecuteAsync(package.Manifest.Id, RequestOrigin.LocalUi, () => sources.IsReviewing(package), target);
    }

    private Task RequestAsync(FixedSessionAction action, SecurityAuditInitiator initiator, Func<bool> admitted)
    {
        var id = action switch
        {
            FixedSessionAction.Lock => "kora.session.lock",
            FixedSessionAction.Shutdown => "kora.computer.shutdown",
            FixedSessionAction.Restart => "kora.computer.restart",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        if (!sources.OpenPackage(id, admitted))
        { throw new InvalidOperationException("The complete immutable package review could not be opened."); }
        if (initiator == SecurityAuditInitiator.ModelSuggestion && action != FixedSessionAction.Lock)
        {
            Status = "Model suggestion opened source inspection only. A fresh original-user Review/grant and run action is required.";
            main.ReportHostInteractionFailure(Status);
            return Task.CompletedTask;
        }
        if (!active.IsCompleted) { throw new InvalidOperationException("A bundled operation already owns the native review."); }
        var origin = HostActivity.Current?.Request.Origin ?? initiator switch
        {
            SecurityAuditInitiator.VoiceCommand => RequestOrigin.ActivatedVoice,
            SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand => RequestOrigin.LocalUi,
            _ => RequestOrigin.HostSystem,
        };
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        { throw new InvalidOperationException("Lock requires the original user's admitted input channel; model output is not approval."); }
        active = ExecuteAsync(id, origin, admitted);
        return active;
    }

    private async Task ExecuteAsync(string id, RequestOrigin origin, Func<bool> admitted, WorkSessionAuthorization? target = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var cancellation = new CancellationTokenSource();
        lifetime = cancellation;
        var control = new HostRequest(new(Guid.NewGuid()), target?.SessionId ?? new(Guid.NewGuid()), new(Guid.NewGuid()), origin);
        var request = new HostRequest(control.RequestId, control.SessionId, control.TaskId, origin, new(Guid.NewGuid()));
        var cause = HostActivity.Current;
        using var activity = HostActivity.BeginRoot(control, HostActivityLayer.Desktop, HostOperation.Request,
            cause is null ? [] : [new ActivityLink(cause.Activity!.Context)]);
        var resumeOriginal = HostActivity.CaptureContinuation(HostActivityLayer.Desktop, HostOperation.Request);
        HostTaskRecord? record = null;
        BundledFixedActionInvocation? invocation = null;
        var ownerRevision = ownershipRevision();
        var call = main.CallPolicyRevision;
        try
        {
            var script = Resolve(id);
            bool Current() => !disposed && !activity.Activity!.IsStopped && activity.Outcome == HostOperationOutcome.Unknown
                && !cancellation.IsCancellationRequested && admitted() && ownsHost()
                && ownerRevision == ownershipRevision() && main.CallPolicyRevision == call
                && main.CanRevealPrivatePresentation && main.IsSkillReviewCallEligible && sources.IsReviewing(script.Package);
            if (!Current()) { throw new InvalidOperationException("The original package review is not current."); }
            if (worker.QualifiedImplementationDigest is null)
            { throw new InvalidOperationException("The contained PowerShell runtime and typed bridge are not qualified on this installation. "
                + sources.RuntimeDescription + " Review is available; execution and grants remain unavailable."); }
            record = await store.RecordControlIntentAsync(control, cancellation.Token);
            var session = target ?? await store.CreateBundledControlSessionAsync(control, Current, cancellation.Token);
            record = await store.AdmitBundledControlDispatchAsync(control, session.Generation, Current, cancellation.Token);
            reviewedControlSession = session;
            sources.SetControlSessionAvailable(true);
            invocation = new(script, worker, broker, authorization,
                async (proposal, generation, current, token) => await Dispatcher.UIThread.InvokeAsync(async () =>
                    await questions.RequestBundledPermissionAsync(proposal, script,
                        BundledActionBinding.Create(script, worker.QualifiedImplementationDigest
                            ?? throw new InvalidOperationException("The runtime qualification retired."),
                            broker.CurrentIdentityDigest ?? throw new InvalidOperationException("Windows identity retired.")),
                        generation, current, state =>
                        {
                            questionWindow = new(state, renderer);
                            questionWindow.Show(owner());
                            return Task.CompletedTask;
                        }, token)),
                () => Resolve(id), request, session.Generation, Current,
                async token => await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    using var original = resumeOriginal();
                    var prepared = await main.PrepareBundledResourcesAsync(script.Action, Current,
                        current => lifecycle.PrepareComputerPowerAsync(
                            script.Action == FixedSessionAction.Shutdown
                                ? ApplicationLifecycleOperation.ComputerShutdown : ApplicationLifecycleOperation.ComputerRestart,
                            request, current, token));
                    return prepared is null ? null : new ApplicationLifecycleShutdown(() => prepared.IsCurrent,
                        async () => await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            using var joining = resumeOriginal();
                            await prepared.StopAndJoinAsync();
                        }),
                        () => Dispatcher.UIThread.Invoke(prepared.Dispose));
                }),
                TimeProvider.System);
            Status = "Reviewing exact scripts and effect. Nothing has been requested from Windows.";
            FixedSessionEffectResult result;
            using (var executing = HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Tool,
                [new ActivityLink(activity.Activity!.Context)]))
            {
                result = await invocation.ExecuteAsync(cancellation.Token);
                executing.Complete(result.Outcome == FixedSessionEffectOutcome.RequestAccepted
                    ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            }
            Status = result.Outcome switch
            {
                FixedSessionEffectOutcome.RequestAccepted => "Windows accepted the exact request. Physical lock or computer-power completion is not observed. Do not retry.",
                FixedSessionEffectOutcome.Denied => "The exact Windows request was denied. No accepted native effect was reported.",
                FixedSessionEffectOutcome.Failed => $"Windows rejected the request (native error {result.NativeError}). No acceptance was reported.",
                _ => "The native effect or privilege restoration is unconfirmed. Do not retry or assume rollback.",
            };
            var terminal = await tasks.RecordOutcomeAsync(record,
                result.Outcome is FixedSessionEffectOutcome.RequestAccepted or FixedSessionEffectOutcome.Unconfirmed
                    ? HostTaskState.Unknown : HostTaskState.Failed, CancellationToken.None);
            var readback = await store.ReadTaskAsync(request.SessionId, request.TaskId, CancellationToken.None);
            if (readback?.Task != terminal)
            { throw new InvalidOperationException("The native request terminal readback is unconfirmed. Do not retry."); }
            record = terminal;
            activity.Complete(result.Outcome == FixedSessionEffectOutcome.RequestAccepted
                ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            if (CanPublish()) { main.ReportHostInteractionFailure(Status); }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failed(logger, exception);
            Status = invocation?.NativeRequestStarted == true
                ? "The native effect or required terminal evidence is unconfirmed. Do not retry or assume rollback."
                : "Bundled execution did not reach a native effect request. " + exception.Message;
            if (record is { IsTerminal: false })
            {
                try
                {
                    var terminal = await tasks.RecordOutcomeAsync(record,
                        invocation?.NativeRequestStarted == true ? HostTaskState.Unknown : HostTaskState.Failed,
                        CancellationToken.None);
                    var readback = await store.ReadTaskAsync(request.SessionId, request.TaskId, CancellationToken.None);
                    if (readback?.Task != terminal)
                    { throw new InvalidOperationException("The failed bundled operation terminal readback is unconfirmed.", exception); }
                }
                catch (Exception storageFailure) when (storageFailure is not OutOfMemoryException)
                { Failed(logger, storageFailure); Status += " Required terminal evidence is also unconfirmed."; }
            }
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
            if (CanPublish()) { main.ReportHostInteractionFailure(Status); }
        }
        finally
        {
            questionWindow?.Close();
            questionWindow = null;
            if (ReferenceEquals(lifetime, cancellation)) { lifetime = null; }
        }

        bool CanPublish() => !disposed && main.CanRevealPrivatePresentation && ownsHost()
            && ownerRevision == ownershipRevision() && main.CallPolicyRevision == call && admitted();
    }

    private static BundledFixedScript Resolve(string id) =>
        EmbeddedFixedActionCatalogue.Resolve(EmbeddedSkillCatalogue.Load().Packages.Single(package =>
            string.Equals(package.Manifest.Id, id, StringComparison.Ordinal)));

    private async Task JoinAsync()
    {
        lifetime?.Cancel();
#pragma warning disable VSTHRD003 // Joins this controller's existing operation; the operation never joins itself.
        await active;
#pragma warning restore VSTHRD003
        if (!worker.IsQuiescent)
        { throw new InvalidOperationException("The bundled worker's physical resource release is unconfirmed."); }
    }

    private void OnPrivacyClosed(object? sender, EventArgs args) => lifetime?.Cancel();

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        lifetime?.Cancel();
        sources.BindBundledExecution(null);
        sources.SetControlSessionAvailable(false);
        main.BindBundledActions(null);
        main.PrivacyClosureRequested -= OnPrivacyClosed;
    }
}