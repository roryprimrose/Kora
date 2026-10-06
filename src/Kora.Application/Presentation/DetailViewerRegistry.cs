using System.Diagnostics;
using Kora.Core.Presentation;

namespace Kora.Application.Presentation;

public sealed class DetailViewerRegistry
{
    private static readonly ActivitySource ActivitySource =
        new("Kora.Application", typeof(DetailViewerRegistry).Assembly.GetName().Version!.ToString());
    private readonly Dictionary<DetailContentReference, DetailViewerState> viewers = [];

    public DetailViewerState Open(AdmittedDetailContent content, bool canAccess)
    {
        using var presentation = ActivitySource.StartActivity("presentation.open");
        presentation?.SetStatus(ActivityStatusCode.Error);
        ArgumentNullException.ThrowIfNull(content);
        presentation?.SetTag("kora.item.id", content.Reference.ItemId.Value);
        presentation?.SetTag("kora.item.revision", content.Reference.Revision);
        presentation?.SetTag("kora.presentation.profile", NativeDetailProfile.Name);
        if (!canAccess)
        {
            throw new InvalidOperationException("Details are unavailable while the host privacy/input gate is closed.");
        }
        if (viewers.TryGetValue(content.Reference, out var existing))
        {
            if (existing.Content is null || !existing.Content.IsSameSnapshot(content))
            {
                throw new InvalidDataException("An immutable detail reference cannot be replaced; resolve a new revision.");
            }
            presentation?.SetStatus(ActivityStatusCode.Ok);
            return existing;
        }
        if (viewers.Count >= NativeDetailProfile.MaximumOpenViewers)
        {
            throw new InvalidOperationException("Eight detail viewers are already open. Close a viewer before opening another item.");
        }
        var viewer = new DetailViewerState(content);
        viewers.Add(content.Reference, viewer);
        presentation?.SetStatus(ActivityStatusCode.Ok);
        return viewer;
    }

    public void Close(DetailContentReference reference)
    {
        if (viewers.Remove(reference, out var viewer))
        {
            viewer.Close();
        }
    }

    public void ClearForPrivacy()
    {
        foreach (var viewer in viewers.Values)
        {
            viewer.Close();
        }
        viewers.Clear();
    }
}
