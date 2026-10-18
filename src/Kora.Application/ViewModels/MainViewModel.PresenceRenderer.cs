using System.Collections.ObjectModel;

using Kora.Core.Configuration;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private static readonly ReadOnlyCollection<PresenceRendererChoice> RendererChoices =
        Array.AsReadOnly<PresenceRendererChoice>([new("Avalonia", false), new("Native (experimental)", true)]);
    private bool isNativePresenceRenderer;
    private bool nativePresenceRendererObserved;
    private string presenceRendererStatus = "The presence renderer has not been initialized.";

    public IReadOnlyList<PresenceRendererChoice> PresenceRenderers => RendererChoices;

    public bool IsNativePresenceRenderer
    {
        get
        {
            if (!nativePresenceRendererObserved)
            {
                isNativePresenceRenderer = appearanceConfiguration.Get(AppearanceOption.NativePresenceRenderer).Value.GetToggle();
                nativePresenceRendererObserved = true;
            }
            return isNativePresenceRenderer;
        }
        set => QueueAppearance(AppearanceOption.NativePresenceRenderer, new AppearanceValue.Toggle(value),
            nameof(IsNativePresenceRenderer));
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
