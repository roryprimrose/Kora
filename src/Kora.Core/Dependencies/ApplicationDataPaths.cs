namespace Kora.Core.Dependencies;

public sealed class ApplicationDataPaths : IApplicationDataPaths
{
    public string LocalRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Kora");

    public string RoamingRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Kora");
}