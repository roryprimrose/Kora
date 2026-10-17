using System.Collections.ObjectModel;

using Kora.Core.Auditing;
using Kora.Core.Configuration;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private static readonly ReadOnlyCollection<PresenceRendererChoice> RendererChoices =
        Array.AsReadOnly<PresenceRendererChoice>([new("Avalonia", false), new("Native (experimental)", true)]);
    private bool isNativePresenceRenderer;
    private string presenceRendererStatus = "The presence renderer has not been initialized.";

    public IReadOnlyList<PresenceRendererChoice> PresenceRenderers => RendererChoices;

    public bool IsNativePresenceRenderer
    {
        get => appearanceConfiguration.Get(AppearanceOption.NativePresenceRenderer).Value.GetToggle();
        set => ApplyAppearance(AppearanceOption.NativePresenceRenderer, new AppearanceValue.Toggle(value),
            SecurityAuditInitiator.LocalUser, nameof(IsNativePresenceRenderer));
    }

    public PresenceRendererChoice SelectedPresenceRenderer
    {
        get => RendererChoices[IsNativePresenceRenderer ? 1 : 0];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            IsNativePresenceRenderer = value.IsNative;
            OnPropertyChanged(nameof(SelectedPresenceRenderer));
        }
    }

    public string PresenceRendererStatus
    {
        get => presenceRendererStatus;
        private set => SetProperty(ref presenceRendererStatus, value);
    }

    public void ReportPresenceRendererStatus(string status) => PresenceRendererStatus = status;
}
