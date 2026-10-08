using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

using Kora.Controls;
using Kora.Core.Presentation;
using Kora.Core.Skills;

namespace Kora;

public sealed partial class SkillPackagesWindow : Window
{
    private readonly SkillPackageCatalogue catalogue;
    private readonly NativeDetailRenderer renderer;

    public SkillPackagesWindow() => throw new InvalidOperationException("Use the host-owned immutable skill inspection route.");

    internal SkillPackagesWindow(SkillPackageCatalogue catalogue, NativeDetailRenderer renderer)
    {
        this.catalogue = catalogue;
        this.renderer = renderer;
        AvaloniaXamlLoader.Load(this);
        var selector = this.FindControl<ComboBox>("PackageSelector")!;
        selector.ItemsSource = catalogue.Packages.Select(package =>
            $"{package.Manifest.Name} ({package.Manifest.Id}) - unavailable").ToArray();
        selector.SelectionChanged += (_, _) =>
        {
            if (selector.SelectedIndex >= 0)
            {
                ShowPackage(selector.SelectedIndex);
            }
        };
        selector.SelectedIndex = 0;
    }

    private void ShowPackage(int index)
    {
        var package = catalogue.Packages[index];
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
    }

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
