using System.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Core.Diagnostics;

public sealed class HostActivity : IDisposable
{
    private static readonly AsyncLocal<HostActivity?> Ambient = new();
    private static readonly ActivitySource CoreSource = CreateSource("Kora.Core");
    private static readonly ActivitySource ApplicationSource = CreateSource("Kora.Application");
    private static readonly ActivitySource WindowsSource = CreateSource("Kora.Windows");
    private static readonly ActivitySource DesktopSource = CreateSource("Kora.Desktop");
    private readonly HostActivity? prior;
    private readonly Activity? priorActivity;
    private bool disposed;

    private HostActivity(HostRequest request, HostActivityLayer layer, HostOperation operation,
        bool root, IEnumerable<ActivityLink>? links, Guid? correlationId = null, Guid? approvalId = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ConfigureW3C();
        Request = request;
        CorrelationId = correlationId;
        ApprovalId = approvalId;
        prior = Ambient.Value;
        priorActivity = Activity.Current;
        if (root)
        {
            // Incoming ambient context is not a host parent or a session selector.
            Activity.Current = null;
        }
        try
        {
            Activity = Source(layer).StartActivity(Name(operation), ActivityKind.Internal,
                default(ActivityContext), tags:
                [
                    new("kora.request.id", request.RequestId.Value),
                    new("kora.session.id", request.SessionId.Value),
                    new("kora.task.id", request.TaskId.Value),
                    new("kora.origin", request.Origin.ToString()),
                ], links: links);
            Ambient.Value = this;
            if (correlationId is { } correlation)
            {
                Activity?.SetTag("kora.audit.correlation.id", correlation);
            }
            if (approvalId is { } approval)
            {
                Activity?.SetTag("kora.approval.id", approval);
            }
            if (request.InvocationId is { } invocation)
            {
                Activity?.SetTag("kora.invocation.id", invocation.Value);
            }
        }
        catch
        {
            Activity.Current = priorActivity;
            throw;
        }
    }

    public Activity? Activity { get; }
    public HostRequest Request { get; }
    public Guid? CorrelationId { get; }
    public Guid? ApprovalId { get; }
    public HostOperationOutcome Outcome { get; private set; } = HostOperationOutcome.Unknown;

    public static void ConfigureW3C()
    {
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;
    }

    public static HostActivity BeginRoot(HostRequest request, HostActivityLayer layer,
        HostOperation operation, IEnumerable<ActivityLink>? links = null) =>
        new(request, layer, operation, root: true, links);

    public static HostActivity BeginChild(HostActivityLayer layer, HostOperation operation)
    {
        var current = RequireCurrent();
        return new HostActivity(current.Request, layer, operation, root: false, links: null,
            current.CorrelationId, current.ApprovalId);
    }

    public static HostActivity BeginAudit(HostRequest request, Kora.Core.Auditing.SecurityAuditEvent audit,
        IEnumerable<ActivityLink>? links = null) =>
        new(request, HostActivityLayer.Application, HostOperation.Policy,
            root: Current?.Request != request, links, audit.CorrelationId, audit.ApprovalId);

    public static HostActivity RequireCurrent()
    {
        var current = Ambient.Value;
        if (current is null || current.disposed || current.Activity is null
            || !ReferenceEquals(System.Diagnostics.Activity.Current, current.Activity))
        {
            throw new InvalidOperationException("A live host-resolved activity is required.");
        }
        return current;
    }

    public static HostActivity? Current =>
        Ambient.Value is { disposed: false, Activity: not null } current
        && ReferenceEquals(System.Diagnostics.Activity.Current, current.Activity) ? current : null;

    public void Complete(HostOperationOutcome outcome)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }
        Outcome = outcome;
        Activity?.SetStatus(outcome == HostOperationOutcome.Completed
            ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        Activity?.SetTag("kora.outcome", outcome.ToString());
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        if (!ReferenceEquals(Ambient.Value, this))
        {
            throw new InvalidOperationException("Host activity scopes must be disposed in order.");
        }
        if (Outcome == HostOperationOutcome.Unknown)
        {
            Complete(HostOperationOutcome.Unknown);
        }
        Activity?.Dispose();
        disposed = true;
        Ambient.Value = prior;
        Activity.Current = priorActivity;
    }

    private static ActivitySource CreateSource(string name) =>
        new(name, typeof(HostActivity).Assembly.GetName().Version!.ToString());

    private static ActivitySource Source(HostActivityLayer layer) => layer switch
    {
        HostActivityLayer.Core => CoreSource,
        HostActivityLayer.Application => ApplicationSource,
        HostActivityLayer.Windows => WindowsSource,
        HostActivityLayer.Desktop => DesktopSource,
        _ => throw new ArgumentOutOfRangeException(nameof(layer)),
    };

    private static string Name(HostOperation operation) => operation switch
    {
        HostOperation.Startup => "host.startup",
        HostOperation.Request => "session.request",
        HostOperation.Policy => "policy.evaluate",
        HostOperation.Runtime => "runtime.request",
        HostOperation.Tool => "tool.invoke",
        HostOperation.Storage => "storage.commit",
        HostOperation.Presentation => "presentation.update",
        HostOperation.Evidence => "evidence.query",
        HostOperation.Retention => "retention.run",
        HostOperation.Recovery => "recovery.reconcile",
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };
}
