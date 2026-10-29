using System.Globalization;

using Kora.Core.Authorization;

namespace Kora.Application.Interaction;

public sealed record ExactOperationPermissionGrant(ExactGrantInspection Inspection) : PermissionGrantRecord
{
    public override string Name => Inspection.Grant.Id.Value.ToString("D");
    public override string Summary => Inspection.Grant.ApprovedProposal.Binding.ActionId;
    public override string Detail => Inspection.Grant.Scope + " | " + Inspection.Grant.Status
        + " | revision " + Inspection.Grant.Revision.Value.ToString(CultureInfo.InvariantCulture);
    public override PermissionGrantTarget? Target => Inspection.Grant.ApprovedProposal.Binding.Target;
}
