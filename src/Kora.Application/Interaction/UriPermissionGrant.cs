using Kora.Core.Authorization;
using Kora.Core.Network;

namespace Kora.Application.Interaction;

public sealed record UriPermissionGrant : PermissionGrantRecord
{
    public UriPermissionGrant(string pattern) => Pattern = PreapprovedUriPattern.Parse(pattern).Value;
    public string Pattern { get; }
    public override string Name => Pattern;
    public override string Summary => "Web-page address preapproval";
    public override string Detail => "Device-local URI rule; not an exact operation approval";
    public override PermissionGrantTarget Target { get; } = new(PermissionGrantArtifactKind.Uri);
}
