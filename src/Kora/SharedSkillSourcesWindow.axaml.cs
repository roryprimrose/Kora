using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Kora.Core.Skills;

namespace Kora;

public sealed partial class SharedSkillSourcesWindow : Window
{
    internal SharedSkillSourcesView View { get; }
    public SharedSkillSourcesWindow() => throw new InvalidOperationException("Use the admitted local shared source inspection route.");
    internal SharedSkillSourcesWindow(Func<Task> register, Func<Task> refresh, Func<Task> discover, Func<Task> verify,
        Func<Task> unregister, Func<bool>? current = null)
    {
        InitializeComponent();
        View = new(this, register, refresh, discover, verify, unregister, current);
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
    internal IReadOnlyList<SharedSkillSource> RegisteredSources => View.RegisteredSources;
    internal SharedSkillSource? SelectedSource => View.SelectedSource;
    internal SharedSkillSnapshot? SelectedPackage => View.SelectedPackage;
    internal long CatalogueGeneration => View.CatalogueGeneration;
    internal void SetSources(IReadOnlyList<SharedSkillSource> sources) => View.SetSources(sources);
    internal void SetCatalogue(SharedSkillCatalogue catalogue) => View.SetCatalogue(catalogue);
    internal void Report(string message) => View.Report(message);
    internal void SetBusy(bool busy) => View.SetBusy(busy);
    internal void SetWithdrawalBusy(bool busy) => View.SetWithdrawalBusy(busy);
    internal void ClearCatalogue() => View.ClearCatalogue();
    internal void ClearPrivateContent() => View.ClearPrivateContent();
    internal void BindInvocation(Func<Task> review) => View.BindInvocation(review);
    internal void BindToolInvocation(Func<Task> review) => View.BindToolInvocation(review);
    internal (SharedSkillSource Source, SharedSkillSnapshot Snapshot)? ResolveInvocation(string selection, SharedSkillSource source) =>
        View.ResolveInvocation(selection, source);
    internal Task<bool> ConfirmWithdrawalAsync(SharedSkillSource source, CancellationToken token) => View.ConfirmWithdrawalAsync(source, token);
}
