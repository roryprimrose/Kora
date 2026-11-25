using Kora.Core.Customizations;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using NativeSelection = Kora.Core.Dependencies.CopilotCustomizationSelection;

namespace Kora.Application.Definitions;

/// <summary>The native Copilot view of one shared volatile review.</summary>
public sealed class CopilotCustomizationReview
{
    private readonly HostedCustomizationReview review;
    internal CopilotCustomizationReview(HostedCustomizationReview review) => this.review = review;
    public Guid Id => review.Id;
    public CustomizationSnapshot Snapshot => review.Snapshot;
    public NativeSelection Selection => review.NativeSelection;
    public SessionWorkspaceEntry Target => review.Target;
    public string Body => review.Body;
    public RequestOrigin Origin => review.Origin;
    public DateTimeOffset ExpiresAt => review.ExpiresAt;
    public bool HostedSourceApproved => review.HostedSourceApproved;
    public string Text => review.Text;
}
