namespace Kora.Application.Interaction;

public sealed record PermissionGrantRow(PermissionGrantRecord Grant, string GroupHeading)
{
    public bool HasGroupHeading => GroupHeading.Length != 0;
}
