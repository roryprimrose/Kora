namespace Kora.Core.Dependencies;

public interface IApplicationDataPaths
{
    string LocalRoot { get; }

    string RoamingRoot { get; }
}