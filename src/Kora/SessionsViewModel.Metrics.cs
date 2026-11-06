using Kora.Core.Dependencies;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private SessionModelMetrics? sessionMetrics;
    private SessionWorkspaceEntry? metricsTarget;
    private long metricsControl;
    public bool CanReadMetrics => CanRead && !refreshingWork && selected is not null && conversation is not null;
    private bool MetricsCurrent => !closed && access.CanInspect && metricsControl == access.ControlRevision
        && metricsTarget == selected && sessionMetrics is not null;
    public string MetricsStatus => MetricsCurrent ? sessionMetrics!.Format()
        : "Open or refresh Session metrics for the selected conversation. No usage is inferred.";
    public IReadOnlyList<ModelUsageSummary> MetricsModels => MetricsCurrent ? sessionMetrics!.Models : [];

    public Task ReadMetricsAsync(Func<bool> presenter) => RunAsync(async () =>
    {
        ClearMetrics();
        var target = RequireSelected();
        var epoch = selectionEpoch;
        var control = access.ControlRevision;
        bool Live() => !closed && access.CanInspect && access.ControlRevision == control
            && selected == target && selectionEpoch == epoch && presenter();
        var snapshot = await (conversation ?? throw new InvalidOperationException("The session metrics owner is unavailable."))
            .ReadMetricsAsync(target, Live, lifetime.Token);
        if (!Live()) { throw new OperationCanceledException("The original metrics presenter was retired."); }
        sessionMetrics = snapshot;
        metricsTarget = target;
        metricsControl = control;
        NotifyMetrics();
    }, passive: true);

    private void ClearMetrics()
    {
        sessionMetrics = null;
        metricsTarget = null;
        NotifyMetrics();
    }

    private void NotifyMetrics()
    {
        OnPropertyChanged(nameof(CanReadMetrics));
        OnPropertyChanged(nameof(MetricsStatus));
        OnPropertyChanged(nameof(MetricsModels));
    }
}
