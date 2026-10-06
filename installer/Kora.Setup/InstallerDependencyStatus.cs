namespace Kora.Setup;

public sealed record InstallerDependencyStatus(InstallerDependencyState State, string Detail)
{
    public bool CanPrepare => State is InstallerDependencyState.Missing
        or InstallerDependencyState.UpdateRequired or InstallerDependencyState.Detected
        or InstallerDependencyState.NotRunning or InstallerDependencyState.NeedsPreparation;

    public string Summary => State switch
    {
        InstallerDependencyState.Checking => "Checking",
        InstallerDependencyState.Installed => "Installed - will be reused",
        InstallerDependencyState.Missing => "Missing",
        InstallerDependencyState.UpdateRequired => "Update required",
        InstallerDependencyState.Detected => "Detected - verification required",
        InstallerDependencyState.NotRunning => "Installed but not running",
        InstallerDependencyState.NeedsPreparation => "Preparation required",
        InstallerDependencyState.Incompatible => "Incompatible - preparation blocked",
        InstallerDependencyState.Failed => "Couldn't verify - preparation blocked",
        _ => throw new InvalidOperationException("Unknown dependency state."),
    };

    public string Display => $"{Summary}: {Detail}";

    public static InstallerDependencyStatus Checking { get; } =
        new(InstallerDependencyState.Checking, "Read-only detection has not completed.");
}
