namespace Kora.Core.Dependencies;

public sealed class SetupTaskLedger
{
    private readonly Dictionary<string, SetupTask> tasks = new(StringComparer.Ordinal);
    private readonly Lock gate = new();

    public event EventHandler? Changed;

    public IReadOnlyList<SetupTask> Tasks
    {
        get
        {
            lock (gate)
            {
                return tasks.Values.ToArray();
            }
        }
    }

    public SetupTask? ActiveTask => Tasks.FirstOrDefault(task => task.State == SetupTaskState.Running);

    public void Start(string id, string name, string detail)
    {
        lock (gate)
        {
            if (tasks.Values.Any(task => task.State == SetupTaskState.Running))
            {
                throw new InvalidOperationException("Another setup task is already running.");
            }

            tasks[id] = new SetupTask(id, name, SetupTaskState.Running, detail);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Update(string id, SetupTaskState state, string detail, int? progressPercentage = null)
    {
        if (progressPercentage is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(progressPercentage));
        }

        lock (gate)
        {
            if (!tasks.TryGetValue(id, out var task))
            {
                throw new InvalidOperationException($"Setup task '{id}' is not registered.");
            }

            tasks[id] = task with
            {
                State = state,
                Detail = detail,
                ProgressPercentage = progressPercentage,
            };
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reconcile(DependencyStatus status)
    {
        lock (gate)
        {
            if (tasks.TryGetValue(status.Id, out var task)
                && task.State == SetupTaskState.Running)
            {
                return;
            }

            var state = status.Readiness switch
            {
                DependencyReadiness.Ready => SetupTaskState.Completed,
                DependencyReadiness.Failed => SetupTaskState.Failed,
                _ => SetupTaskState.NeedsAction,
            };
            tasks[status.Id] = new SetupTask(status.Id, status.Name, state, status.Detail);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(string id)
    {
        bool removed;
        lock (gate)
        {
            removed = tasks.Remove(id);
        }
        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
