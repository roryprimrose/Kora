namespace Kora.Core.Dependencies;

public interface ISetupDependencyProbe : IDependencyProbe
{
    string TaskId { get; }

    string TaskName { get; }
}
