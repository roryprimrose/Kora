using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

using Kora.Controls;
using Kora.Application.Skills;
using Kora.Core.Presentation;
using Kora.Core.Skills;

namespace Kora;

public sealed partial class SkillPackagesWindow : Window
{
    private readonly SkillPackageCatalogue catalogue;
    private readonly NativeDetailRenderer renderer;
    private readonly Func<bool> current;
    private IReadOnlyList<SkillPackageSnapshot> results = [];
    private bool updatingResults;
    private bool closed;
    internal event Action<SkillPackageSnapshot?>? PackageChanged;

    public SkillPackagesWindow() => throw new InvalidOperationException("Use the host-owned immutable skill inspection route.");

    internal SkillPackagesWindow(SkillPackageCatalogue catalogue, NativeDetailRenderer renderer, Action? sharedSources = null,
        Func<bool>? current = null, Action<SkillPackageSnapshot>? execute = null,
        Action<SkillPackageSnapshot>? executeBound = null, Action<SkillPackageSnapshot>? runtimeSetup = null)
    {
        this.catalogue = catalogue;
        this.renderer = renderer;
        this.current = current ?? (() => false);
        AvaloniaXamlLoader.Load(this);
        this.FindControl<Button>("ReviewBundledRuntime")!.IsEnabled = runtimeSetup is not null;
        this.FindControl<Button>("ReviewBundledRuntime")!.Click += (_, _) =>
        {
            if (!closed && this.current() && SelectedPackage is { } selected) { runtimeSetup?.Invoke(selected); }
        };
        this.FindControl<Button>("ReviewBundledExecution")!.IsEnabled = execute is not null;
        this.FindControl<Button>("ReviewBundledExecution")!.Click += (_, _) =>
        {
            if (!closed && this.current() && SelectedPackage is { } selected) { execute?.Invoke(selected); }
        };
        this.FindControl<Button>("ReviewBoundBundledExecution")!.IsEnabled = false;
        this.FindControl<Button>("ReviewBoundBundledExecution")!.Click += (_, _) =>
        {
            if (!closed && this.current() && SelectedPackage is { } selected) { executeBound?.Invoke(selected); }
        };
        this.FindControl<Button>("SharedSources")!.Click += (_, _) => { if (!closed) { sharedSources?.Invoke(); } };
        this.FindControl<Button>("SharedSources")!.IsEnabled = sharedSources is not null;
        var selector = this.FindControl<ComboBox>("PackageSelector")!;
        var filter = this.FindControl<ComboBox>("PackageFilter")!;
        filter.ItemsSource = new[] { "All", "Runtime unavailable", "Runtime available" };
        filter.SelectedIndex = 0;
        var order = this.FindControl<ComboBox>("PackageOrder")!;
        order.ItemsSource = new[] { "Source order", "Name ascending (ordinal)", "Name descending (ordinal)" };
        order.SelectedIndex = 0;
        this.FindControl<Button>("SearchPackages")!.Click += (_, _) => Search();
        this.FindControl<Button>("ClearPackageSearch")!.Click += (_, _) =>
        {
            RetireSearch();
            this.FindControl<TextBox>("PackageQuery")!.Text = string.Empty;
            filter.SelectedIndex = 0;
            order.SelectedIndex = 0;
            Search();
        };
        foreach (var name in new[] { "PackageQuery" })
        {
            this.FindControl<TextBox>(name)!.PropertyChanged += (_, change) =>
            { if (change.Property == TextBox.TextProperty) { RetireSearch(); } };
        }
        filter.SelectionChanged += (_, _) => RetireSearch();
        order.SelectionChanged += (_, _) => RetireSearch();
        KeyDown += (_, args) => { if (args.Key == Key.Escape && !args.Handled) { Close(); args.Handled = true; } };
        selector.SelectionChanged += (_, _) =>
        {
            if (!updatingResults) { ShowPackage(); }
        };
        Closed += (_, _) =>
        {
            ClearPrivateContent();
            IdentityFinder.Dispose();
        };
        Search();
        selector.SelectedIndex = 0;
    }

    internal void ClearPrivateContent()
    {
        closed = true;
        results = [];
        this.FindControl<ComboBox>("PackageSelector")!.ItemsSource = Array.Empty<string>();
        this.FindControl<TextBox>("PackageQuery")!.Text = string.Empty;
        this.FindControl<ComboBox>("PackageFilter")!.SelectedIndex = 0;
        this.FindControl<ComboBox>("PackageOrder")!.SelectedIndex = 0;
        this.FindControl<TextBlock>("PackageSearchStatus")!.Text = string.Empty;
        ClearSelection();
    }

    internal SkillPackageSnapshot? SelectedPackage
    {
        get
        {
            var index = this.FindControl<ComboBox>("PackageSelector")!.SelectedIndex;
            return index >= 0 && index < results.Count ? results[index] : null;
        }
    }

    internal void SetControlSessionAvailable(bool available) =>
        this.FindControl<Button>("ReviewBoundBundledExecution")!.IsEnabled = available;

    internal bool SelectPackage(string id)
    {
        if (closed) { return false; }
        var package = catalogue.Packages.SingleOrDefault(item =>
            string.Equals(item.Manifest.Id, id, StringComparison.Ordinal));
        if (package is null) { return false; }
        SetResults(catalogue.Packages, package);
        ShowPackage();
        return ReferenceEquals(SelectedPackage, package);
    }

    private void Search()
    {
        if (closed) { return; }
        IdentityFinder.Retire();
        var selected = SelectedPackage;
        try
        {
            var matching = SkillCatalogueSearch.Bundled(catalogue,
                this.FindControl<TextBox>("PackageQuery")!.Text ?? string.Empty,
                (SkillInvocationFilter)this.FindControl<ComboBox>("PackageFilter")!.SelectedIndex,
                (SkillCatalogueOrder)this.FindControl<ComboBox>("PackageOrder")!.SelectedIndex);
            SetResults(matching, selected);
            this.FindControl<TextBlock>("PackageSearchStatus")!.Text =
                $"{results.Count} of {catalogue.Packages.Count} admitted bundled snapshots. Invocation remains unavailable."
                + (selected is not null && SelectedPackage is null ? " Selection excluded; private source panes cleared. Select a result explicitly." : string.Empty);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            SetResults([], null);
            this.FindControl<TextBlock>("PackageSearchStatus")!.Text = $"Search rejected: {exception.Message} No results or selection published.";
        }
    }

    private void RetireSearch()
    {
        if (closed) { return; }
        SetResults([], null);
        this.FindControl<TextBlock>("PackageSearchStatus")!.Text =
            "Filters or order edited; selection and private source panes cleared. Choose Search to filter and order admitted snapshots without reading resources.";
    }

    private void SetResults(IReadOnlyList<SkillPackageSnapshot> matching, SkillPackageSnapshot? selected)
    {
        updatingResults = true;
        try
        {
            results = matching;
            var selector = this.FindControl<ComboBox>("PackageSelector")!;
            selector.ItemsSource = results.Select(package =>
                $"{package.Manifest.Name} ({package.Manifest.Id}) - runtime unavailable").ToArray();
            selector.SelectedIndex = results.ToList().FindIndex(package => ReferenceEquals(package, selected));
        }
        finally { updatingResults = false; }
        if (SelectedPackage is null) { ClearSelection(); }
    }

    private void ClearSelection()
    {
        PackageChanged?.Invoke(null);
        IdentityFinder.Retire();
        this.FindControl<TextBlock>("PackageIdentity")!.Text = string.Empty;
        var tabs = this.FindControl<TabControl>("SourceTabs")!;
        foreach (var tab in tabs.Items.OfType<TabItem>())
        {
            if (tab.Content is not DockPanel panel) { continue; }
            foreach (var reader in panel.Children.OfType<TextBox>()) { reader.Text = string.Empty; }
            foreach (var identity in panel.Children.OfType<TextBlock>()) { identity.Text = string.Empty; }
            tab.Content = null;
        }
        tabs.ItemsSource = Array.Empty<TabItem>();
    }

    private void ShowPackage()
    {
        ClearSelection();
        if (closed || SelectedPackage is not { } package) { return; }
        this.FindControl<TextBlock>("PackageIdentity")!.Text =
            $"Bundled first-party source | {package.Manifest.Id} | {package.Manifest.Version}\n"
            + $"Action: {package.Manifest.Action}; entry: {package.Manifest.EntryPoint}; parameter contract: {package.Manifest.ParameterContract}\n"
            + $"Requested runtime: {package.Manifest.Runtime}; dependencies: {string.Join(", ", package.Manifest.Dependencies)}\n"
            + $"Helper load order: {string.Join(", ", package.Manifest.HelperLoadOrder)}\n"
            + $"{SkillPackageDigest.ScriptSetVersion}: {package.ScriptSetDigest}\n"
            + $"{SkillPackageDigest.DefinitionVersion}: {package.DefinitionDigest}\n"
            + $"Declared resources: {package.DeclaredResourceDigest}\n"
            + $"{SkillPackageSnapshot.UnavailableReason}\n{SkillPackageSnapshot.TransitiveGaps}\n{SkillPackageSnapshot.TransitiveDisclosure}";
        this.FindControl<TabControl>("SourceTabs")!.ItemsSource = CreateSourceTabs(catalogue, package, renderer);
        PackageChanged?.Invoke(package);
        IdentityFinder.Admit(this.FindControl<TextBlock>("PackageIdentity")!.Text!,
            () => !closed && current() && ReferenceEquals(SelectedPackage, package));
    }

    private IdentityMetadataFindPane IdentityFinder => this.FindControl<IdentityMetadataFindPane>("IdentityFind")!;

    internal static TabItem[] CreateSourceTabs(SkillPackageCatalogue catalogue, SkillPackageSnapshot package,
        NativeDetailRenderer renderer) => package.Files.Select(file =>
        {
            var document = renderer.Render(file.Text, DetailContentKind.PlainText);
            var shared = catalogue.Dependents(file.ResourceId).Count > 1 ? " (shared)" : string.Empty;
            var identity = new NamedTextBlock
            {
                Text = $"{file.ResourceId}\nSHA-256: {file.Digest} | {file.Bytes.Length} original bytes | {document.Status}",
                TextWrapping = TextWrapping.Wrap,
                [DockPanel.DockProperty] = Dock.Top,
            };
            Avalonia.Automation.AutomationProperties.SetName(identity, $"{file.Name} identity, digest and byte count");
            var source = new TextBox
            {
                Text = document.Source,
                IsReadOnly = true,
                AcceptsReturn = true,
                IsUndoEnabled = false,
                FontFamily = new FontFamily("Consolas"),
                TextWrapping = TextWrapping.NoWrap,
                ContextMenu = null,
                ContextFlyout = null,
            };
            Avalonia.Automation.AutomationProperties.SetName(source, $"{file.Name} exact immutable source");
            return new TabItem
            {
                Header = file.Name + shared,
                Content = new DockPanel { Children = { identity, source } },
            };
        }).ToArray();
}