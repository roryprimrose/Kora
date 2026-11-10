using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Kora.Core.Skills;

namespace Kora;

public sealed partial class SkillPackagesWindow : Window
{
    internal SkillPackagesView View { get; }
    public SkillPackagesWindow() => throw new InvalidOperationException("Use the host-owned immutable skill inspection route.");
    internal SkillPackagesWindow(SkillPackageCatalogue catalogue, NativeDetailRenderer renderer, Action? sharedSources = null,
        Func<bool>? current = null, Action<SkillPackageSnapshot>? execute = null,
        Action<SkillPackageSnapshot>? executeBound = null, Action<SkillPackageSnapshot>? runtimeSetup = null)
    {
        InitializeComponent();
        View = new(this, catalogue, renderer, sharedSources, current, execute, executeBound, runtimeSetup);
        Content = View;
        View.CloseRequested += (_, _) => Close();
        Closed += (_, _) => View.Dispose();
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (!ReferenceEquals(args.Source, this)) { return; }
            var forwarded = new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = args.Key, KeyModifiers = args.KeyModifiers };
            View.RaiseEvent(forwarded);
            args.Handled = forwarded.Handled;
        }, RoutingStrategies.Tunnel);
    }
    internal T? FindControl<T>(string name) where T : Control => View.FindControl<T>(name);
    internal void ClearPrivateContent() => View.ClearPrivateContent();
    internal SkillPackageSnapshot? SelectedPackage => View.SelectedPackage;
    internal void SetControlSessionAvailable(bool available) => View.SetControlSessionAvailable(available);
    internal bool SelectPackage(string id) => View.SelectPackage(id);
    internal static TabItem[] CreateSourceTabs(SkillPackageCatalogue catalogue, SkillPackageSnapshot package,
        NativeDetailRenderer renderer) => SkillPackagesView.CreateSourceTabs(catalogue, package, renderer);
}
