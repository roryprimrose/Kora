namespace Kora.Core.Dependencies;

public sealed class ApplicationDataPaths : IApplicationDataPaths
{
#if DEBUG
    private const bool DefaultDevelopment = true;
#else
    private const bool DefaultDevelopment = false;
#endif

    public ApplicationDataPaths(bool? developmentBuild = null)
    {
        var isDevelopment = developmentBuild ?? DefaultDevelopment;
        var partition = isDevelopment
            ? Path.Combine("Kora", "Development")
            : "Kora";
        LocalRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), partition);
        RoamingRoot = isDevelopment
            ? LocalRoot
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), partition);
    }

    public string LocalRoot { get; }

    public string RoamingRoot { get; }
}