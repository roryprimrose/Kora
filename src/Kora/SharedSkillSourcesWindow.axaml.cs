using System.Text.Json;
using System.Collections.ObjectModel;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

using Kora.Application.Skills;
using Kora.Controls;
using Kora.Core.Skills;

namespace Kora;

public sealed partial class SharedSkillSourcesWindow : Window
{
    private ReadOnlyCollection<SharedSkillSource> sources = Array.AsReadOnly(Array.Empty<SharedSkillSource>());
    private SharedSkillCatalogue? catalogue;
    private bool closed;
    private IReadOnlyList<SharedSkillSnapshot> results = [];
    private bool updatingResults;
    private readonly Func<bool> current;
    private bool busy;
    internal long CatalogueGeneration { get; private set; }

    public SharedSkillSourcesWindow() => throw new InvalidOperationException("Use the admitted local shared source inspection route.");

    internal SharedSkillSourcesWindow(Func<Task> register, Func<Task> refresh, Func<Task> discover, Func<Task> verify,
        Func<Task> unregister, Func<bool>? current = null)
    {
        this.current = current ?? (() => false);
        AvaloniaXamlLoader.Load(this);
        this.FindControl<TextBox>("SharedSource")!.ContextMenu = null;
        this.FindControl<TextBox>("SharedSource")!.ContextFlyout = null;
        this.FindControl<TextBox>("UninspectedEntries")!.ContextMenu = null;
        this.FindControl<TextBox>("UninspectedEntries")!.ContextFlyout = null;
        this.FindControl<Button>("RegisterSource")!.Click += async (_, _) => { if (!closed) { await register(); } };
        this.FindControl<Button>("RefreshSources")!.Click += async (_, _) => { if (!closed) { await refresh(); } };
        this.FindControl<Button>("DiscoverSource")!.Click += async (_, _) => { if (!closed) { await discover(); } };
        this.FindControl<Button>("VerifyRevision")!.Click += async (_, _) => { if (!closed) { await verify(); } };
        this.FindControl<Button>("UnregisterSource")!.Click += async (_, _) => { if (!closed) { await unregister(); } };
        var filter = this.FindControl<ComboBox>("SharedFilter")!;
        filter.ItemsSource = new[] { "All", "Instruction-compatible", "Instruction-incompatible" };
        filter.SelectedIndex = 0;
        var order = this.FindControl<ComboBox>("SharedOrder")!;
        order.ItemsSource = new[] { "Source order", "Name ascending (ordinal)", "Name descending (ordinal)" };
        order.SelectedIndex = 0;
        this.FindControl<Button>("SearchSharedPackages")!.Click += (_, _) => Search();
        this.FindControl<Button>("ClearSharedSearch")!.Click += (_, _) =>
        {
            RetireSearch();
            this.FindControl<TextBox>("SharedQuery")!.Text = string.Empty;
            filter.SelectedIndex = 0;
            order.SelectedIndex = 0;
            Search();
        };
        foreach (var name in new[] { "SharedQuery" })
        {
            this.FindControl<TextBox>(name)!.PropertyChanged += (_, change) =>
            { if (change.Property == TextBox.TextProperty) { RetireSearch(); } };
        }
        filter.SelectionChanged += (_, _) => RetireSearch();
        order.SelectionChanged += (_, _) => RetireSearch();
        KeyDown += (_, args) => { if (args.Key == Key.Escape && !args.Handled) { Close(); args.Handled = true; } };
        this.FindControl<ComboBox>("SourceSelector")!.SelectionChanged += (_, _) => ClearCatalogue();
        this.FindControl<ComboBox>("SharedPackageSelector")!.SelectionChanged += (_, _) =>
        { if (!updatingResults) { ShowSelectedPackage(); } };
        Closed += (_, _) => { closed = true; ClearPrivateContent(); IdentityFinder.Dispose(); };
    }

    internal IReadOnlyList<SharedSkillSource> RegisteredSources => Array.AsReadOnly(sources.ToArray());

    internal SharedSkillSource? SelectedSource
    {
        get
        {
            var index = this.FindControl<ComboBox>("SourceSelector")!.SelectedIndex;
            return index >= 0 && index < sources.Count ? sources[index] : null;
        }
    }

    internal SharedSkillSnapshot? SelectedPackage
    {
        get
        {
            var index = this.FindControl<ComboBox>("SharedPackageSelector")!.SelectedIndex;
            return index >= 0 && index < results.Count ? results[index] : null;
        }
    }

    internal void SetSources(IReadOnlyList<SharedSkillSource> value)
    {
        if (closed) { throw new InvalidOperationException("The source inspection window is closed."); }
        sources = Array.AsReadOnly(value.ToArray());
        var selector = this.FindControl<ComboBox>("SourceSelector")!;
        selector.ItemsSource = sources.Select(source => $"{JsonSerializer.Serialize(source.ProfileRelativeRoot)} | {source.Id:N}").ToArray();
        selector.SelectedIndex = -1;
        ClearCatalogue();
        Report("Registrations loaded without reading any packages. Select a source and choose List selected source.");
    }

    internal void SetCatalogue(SharedSkillCatalogue value)
    {
        if (closed || SelectedSource != value.Source)
        { throw new InvalidOperationException("The exact selected source inspection is no longer current."); }
        catalogue = value;
        CatalogueGeneration++;
        this.FindControl<TextBox>("SharedQuery")!.Text = string.Empty;
        this.FindControl<ComboBox>("SharedFilter")!.SelectedIndex = 0;
        this.FindControl<ComboBox>("SharedOrder")!.SelectedIndex = 0;
        SetResults(value.Packages, null);
        var selector = this.FindControl<ComboBox>("SharedPackageSelector")!;
        selector.SelectedIndex = busy || value.Packages.Count == 0 ? -1 : 0;
        this.FindControl<TextBlock>("SharedSearchStatus")!.Text = $"{value.Packages.Count} admitted snapshots; All includes unavailable entries. Inspection alone cannot invoke.";
        Report($"{value.Packages.Count} pinned local snapshots. No scripts or references read. Live sources can change; recheck before relying on the displayed revision.");
    }

    internal void Report(string message) => this.FindControl<TextBlock>("SharedStatus")!.Text = message;

    internal void SetBusy(bool busy)
    {
        this.busy = busy;
        if (busy) { IdentityFinder.Retire(); }
        foreach (var name in new[] { "RegisterSource", "RefreshSources", "DiscoverSource", "VerifyRevision", "SearchSharedPackages", "ClearSharedSearch" })
        { this.FindControl<Button>(name)!.IsEnabled = !busy; }
        this.FindControl<ComboBox>("SourceSelector")!.IsEnabled = !busy;
        this.FindControl<ComboBox>("SharedPackageSelector")!.IsEnabled = !busy;
        this.FindControl<TextBox>("SharedQuery")!.IsEnabled = !busy;
        this.FindControl<ComboBox>("SharedFilter")!.IsEnabled = !busy;
        this.FindControl<ComboBox>("SharedOrder")!.IsEnabled = !busy;
    }

    internal void SetWithdrawalBusy(bool busy)
    {
        if (busy) { IdentityFinder.Retire(); }
        this.FindControl<Button>("UnregisterSource")!.IsEnabled = !busy;
    }

    internal void ClearCatalogue()
    {
        CatalogueGeneration++;
        catalogue = null;
        results = [];
        this.FindControl<TextBox>("SharedQuery")!.Text = string.Empty;
        this.FindControl<ComboBox>("SharedFilter")!.SelectedIndex = 0;
        this.FindControl<ComboBox>("SharedOrder")!.SelectedIndex = 0;
        this.FindControl<TextBlock>("SharedSearchStatus")!.Text = string.Empty;
        this.FindControl<ComboBox>("SharedPackageSelector")!.ItemsSource = Array.Empty<string>();
        ClearSelection();
        Report("No admitted catalogue. Select an exact registered source and explicitly choose List selected source.");
    }

    private void ClearSelection()
    {
        IdentityFinder.Retire();
        this.FindControl<TextBlock>("SharedIdentity")!.Text = string.Empty;
        this.FindControl<TextBox>("SharedSource")!.Text = string.Empty;
        this.FindControl<TextBox>("UninspectedEntries")!.Text = string.Empty;
        this.FindControl<Expander>("UninspectedDisclosure")!.IsVisible = false;
    }

    internal void ClearPrivateContent()
    {
        sources = Array.AsReadOnly(Array.Empty<SharedSkillSource>());
        this.FindControl<ComboBox>("SourceSelector")!.ItemsSource = Array.Empty<string>();
        ClearCatalogue();
        Report(string.Empty);
    }

    private void ShowSelectedPackage()
    {
        ClearSelection();
        if (closed || SelectedPackage is not { } package) { return; }
        this.FindControl<TextBlock>("SharedIdentity")!.Text =
            $"{JsonSerializer.Serialize(package.SourceQualifiedIdentity)}\nSource: "
            + $"{JsonSerializer.Serialize(catalogue!.Source.ProfileRelativeRoot + '\\' + package.RelativeFile)}\n"
            + $"Reader: {SharedSkillSnapshot.ReaderVersion} | version: {package.Version ?? "unavailable"}\n"
            + $"SHA-256 exact original bytes: {package.RevisionDigest} | {package.Bytes.Length} bytes\n"
            + $"Compatibility: {(package.IsInstructionCompatible ? "instruction-only compatible" : string.Join(", ", package.UnavailableReasons))}\n"
            + $"Declared tool references: {string.Join(", ", package.DeclaredTools)}\n{SharedSkillSnapshot.InvocationUnavailableReason}";
        this.FindControl<Expander>("UninspectedDisclosure")!.IsVisible = package.UninspectedFiles.Count > 0;
        this.FindControl<TextBox>("UninspectedEntries")!.Text = string.Join(Environment.NewLine,
            package.UninspectedFiles.Select(file => JsonSerializer.Serialize(file)));
        this.FindControl<TextBox>("SharedSource")!.Text = package.Text is null
            || package.UnavailableReasons.Contains("invalid-control-text")
            ? "No safe text projection. Exact original bytes in hexadecimal:\n" + Convert.ToHexString(package.Bytes)
            : package.Text;
        if (!busy)
        {
            var generation = CatalogueGeneration;
            IdentityFinder.Admit(this.FindControl<TextBlock>("SharedIdentity")!.Text!,
                () => !closed && !busy && current() && CatalogueGeneration == generation
                    && ReferenceEquals(SelectedPackage, package));
        }
    }

    private IdentityMetadataFindPane IdentityFinder => this.FindControl<IdentityMetadataFindPane>("IdentityFind")!;

    private void Search()
    {
        if (closed) { return; }
        IdentityFinder.Retire();
        if (catalogue is null)
        {
            ClearCatalogue();
            this.FindControl<TextBlock>("SharedSearchStatus")!.Text = "No admitted catalogue. Explicitly list the selected registered source first.";
            return;
        }
        var selected = SelectedPackage;
        try
        {
            var matching = SkillCatalogueSearch.Shared(catalogue,
                this.FindControl<TextBox>("SharedQuery")!.Text ?? string.Empty,
                (SkillInstructionFilter)this.FindControl<ComboBox>("SharedFilter")!.SelectedIndex,
                (SkillCatalogueOrder)this.FindControl<ComboBox>("SharedOrder")!.SelectedIndex);
            SetResults(matching, selected);
            this.FindControl<TextBlock>("SharedSearchStatus")!.Text =
                $"{results.Count} of {catalogue.Packages.Count} admitted snapshots from the exact selected source. Inspection alone cannot invoke."
                + (selected is not null && SelectedPackage is null ? " Selection excluded; private source panes cleared. Select a result explicitly." : string.Empty);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            SetResults([], null);
            this.FindControl<TextBlock>("SharedSearchStatus")!.Text = $"Search rejected: {exception.Message} No results or selection published.";
        }
    }

    private void RetireSearch()
    {
        if (closed) { return; }
        CatalogueGeneration++;
        SetResults([], null);
        Report("Filters or order edited. No source read or revision recheck performed.");
        this.FindControl<TextBlock>("SharedSearchStatus")!.Text =
            "Filters or order edited; selection and private source panes cleared. Choose Search to filter and order admitted snapshots without reading the source.";
    }

    private void SetResults(IReadOnlyList<SharedSkillSnapshot> matching, SharedSkillSnapshot? selected)
    {
        updatingResults = true;
        try
        {
            results = matching;
            var selector = this.FindControl<ComboBox>("SharedPackageSelector")!;
            selector.ItemsSource = results.Select(package =>
                $"{JsonSerializer.Serialize(package.Name ?? "(declared name unavailable)")} | {package.Version ?? "unknown version"} | "
                + $"{JsonSerializer.Serialize(package.SourceQualifiedIdentity)} | {JsonSerializer.Serialize(package.RelativeFile)} | "
                + (package.IsInstructionCompatible ? "instruction-compatible; invocation unavailable" : "instruction-incompatible; invocation unavailable")).ToArray();
            selector.SelectedIndex = results.ToList().FindIndex(package => ReferenceEquals(package, selected));
        }
        finally { updatingResults = false; }
        if (SelectedPackage is null) { ClearSelection(); }
    }
}
