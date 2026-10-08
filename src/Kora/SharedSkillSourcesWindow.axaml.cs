using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using Kora.Core.Skills;

namespace Kora;

public sealed partial class SharedSkillSourcesWindow : Window
{
    private IReadOnlyList<SharedSkillSource> sources = [];
    private SharedSkillCatalogue? catalogue;

    public SharedSkillSourcesWindow() => throw new InvalidOperationException("Use the admitted local shared source inspection route.");

    internal SharedSkillSourcesWindow(Func<Task> register, Func<Task> refresh, Func<Task> discover, Func<Task> verify)
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<TextBox>("SharedSource")!.ContextMenu = null;
        this.FindControl<TextBox>("SharedSource")!.ContextFlyout = null;
        this.FindControl<TextBox>("UninspectedEntries")!.ContextMenu = null;
        this.FindControl<TextBox>("UninspectedEntries")!.ContextFlyout = null;
        this.FindControl<Button>("RegisterSource")!.Click += async (_, _) => await register();
        this.FindControl<Button>("RefreshSources")!.Click += async (_, _) => await refresh();
        this.FindControl<Button>("DiscoverSource")!.Click += async (_, _) => await discover();
        this.FindControl<Button>("VerifyRevision")!.Click += async (_, _) => await verify();
        this.FindControl<ComboBox>("SourceSelector")!.SelectionChanged += (_, _) => ClearCatalogue();
        this.FindControl<ComboBox>("SharedPackageSelector")!.SelectionChanged += (_, _) => ShowSelectedPackage();
    }

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
            return catalogue is not null && index >= 0 && index < catalogue.Packages.Count ? catalogue.Packages[index] : null;
        }
    }

    internal void SetSources(IReadOnlyList<SharedSkillSource> value)
    {
        sources = value;
        var selector = this.FindControl<ComboBox>("SourceSelector")!;
        selector.ItemsSource = sources.Select(source => $"{JsonSerializer.Serialize(source.ProfileRelativeRoot)} | {source.Id:N}").ToArray();
        selector.SelectedIndex = -1;
        ClearCatalogue();
        Report("Registrations loaded without reading any packages. Select a source and choose List selected source.");
    }

    internal void SetCatalogue(SharedSkillCatalogue value)
    {
        catalogue = value;
        var selector = this.FindControl<ComboBox>("SharedPackageSelector")!;
        selector.ItemsSource = value.Packages.Select(package =>
            $"{JsonSerializer.Serialize(package.Name ?? package.RelativeFile)} | {package.Version ?? "unknown version"} | "
            + (package.IsInstructionCompatible ? "instruction-compatible; invocation unavailable" : "incompatible; unavailable")).ToArray();
        selector.SelectedIndex = value.Packages.Count == 0 ? -1 : 0;
        Report($"{value.Packages.Count} pinned local snapshots. No scripts or references read. Live sources can change; recheck before relying on the displayed revision.");
    }

    internal void Report(string message) => this.FindControl<TextBlock>("SharedStatus")!.Text = message;

    internal void SetBusy(bool busy)
    {
        foreach (var name in new[] { "RegisterSource", "RefreshSources", "DiscoverSource", "VerifyRevision" })
        { this.FindControl<Button>(name)!.IsEnabled = !busy; }
        this.FindControl<ComboBox>("SourceSelector")!.IsEnabled = !busy;
        this.FindControl<ComboBox>("SharedPackageSelector")!.IsEnabled = !busy;
    }

    internal void ClearCatalogue()
    {
        catalogue = null;
        this.FindControl<ComboBox>("SharedPackageSelector")!.ItemsSource = Array.Empty<string>();
        this.FindControl<TextBlock>("SharedIdentity")!.Text = string.Empty;
        this.FindControl<TextBox>("SharedSource")!.Text = string.Empty;
        this.FindControl<TextBox>("UninspectedEntries")!.Text = string.Empty;
        this.FindControl<Expander>("UninspectedDisclosure")!.IsVisible = false;
    }

    internal void ClearPrivateContent()
    {
        sources = [];
        this.FindControl<ComboBox>("SourceSelector")!.ItemsSource = Array.Empty<string>();
        ClearCatalogue();
        Report(string.Empty);
    }

    private void ShowSelectedPackage()
    {
        if (SelectedPackage is not { } package) { return; }
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
    }
}
