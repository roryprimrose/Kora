using Avalonia.Controls;
using Avalonia.Platform;

namespace Kora;

public sealed class AvaloniaCaptionDisplaySource : ICaptionDisplaySource
{
    private readonly Screens screens;
    private readonly Dictionary<Screen, CaptionDisplayLifetime> lifetimes = new(ReferenceEqualityComparer.Instance);
    private CaptionDisplaySnapshot snapshot = new(0, []);
    private bool disposed;

    public AvaloniaCaptionDisplaySource(Screens screens)
    {
        this.screens = screens;
        screens.Changed += OnChanged;
        Observe(forceRevision: true);
    }

    public event EventHandler? Changed;

    public CaptionDisplaySnapshot Capture()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Observe(forceRevision: false);
        return snapshot;
    }

    private void Observe(bool forceRevision)
    {
        var current = screens.All;
        foreach (var departed in lifetimes.Keys.Where(key => !current.Any(screen => ReferenceEquals(screen, key))).ToArray())
        {
            lifetimes.Remove(departed);
        }
        var observations = new List<CaptionDisplayObservation>(current.Count);
        foreach (var screen in current)
        {
            if (!lifetimes.TryGetValue(screen, out var lifetime))
            {
                lifetime = new();
                lifetimes.Add(screen, lifetime);
            }
            observations.Add(new(lifetime, screen.WorkingArea, screen.Scaling, screen.IsPrimary));
        }
        if (forceRevision || !snapshot.Displays.SequenceEqual(observations))
        {
            snapshot = new(checked(snapshot.Revision + 1), observations.AsReadOnly());
        }
    }

    private void OnChanged(object? sender, EventArgs args)
    {
        if (disposed) { return; }
        Observe(forceRevision: true);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        screens.Changed -= OnChanged;
        lifetimes.Clear();
        snapshot = new(checked(snapshot.Revision + 1), []);
        Changed = null;
    }
}
