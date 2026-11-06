namespace Kora.Application.Presentation;

/// <summary>Internal host-only consumer of a newly finalized foreground result.</summary>
internal interface IAutomaticDetailPresentation
{
    /// <summary>Captures the existing native foreground workspace, never opening one.</summary>
    AutomaticDetailLease? CaptureAutomaticDetails();
    /// <summary>Consumes one exact finalized intent without activation.</summary>
    bool PublishAutomaticDetails(AutomaticDetailIntent intent);
}
