using Avalonia.Controls;
using Avalonia.Layout;

namespace Kora;

internal sealed partial class AgentRunWindow
{
    internal ComboBox SkillSources { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    internal ComboBox Skills { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    internal Button SkillDeclaration { get; } = new() { Content = "Review selected shared skill declaration", IsEnabled = false };
    internal Button SkillConsent { get; } = new() { Content = "Consent to read declared skill Markdown", IsEnabled = false };
    internal Button SkillEnable { get; } = new() { Content = "Enable this complete skill review once", IsEnabled = false };

    private void AddSharedSkillControls(StackPanel panel, Action<string> run)
    {
        panel.Children.Add(new TextBlock
        {
            Text = "v3 can use one explicitly selected registered shared skill as inert LOCAL guidance. "
                + "Choose its source and catalogue item, review the profile and declaration, separately consent to Markdown reads, "
                + "review every complete file, enable once, then separately confirm the agent invocation. "
                + "No registration, writing, executable skill, nested run or hosted use is available here.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        var sources = new Button { Content = "Refresh registered shared skill sources" };
        sources.Click += (_, _) => run("SkillSources");
        panel.Children.Add(sources);
        panel.Children.Add(SkillSources);
        var catalogue = new Button { Content = "Read selected source catalogue" };
        catalogue.Click += (_, _) => run("SkillCatalogue");
        panel.Children.Add(catalogue);
        panel.Children.Add(Skills);
        foreach (var (button, action) in new[] { (SkillDeclaration, "SkillDeclaration"), (SkillConsent, "SkillConsent"), (SkillEnable, "SkillEnable") })
        {
            button.Click += (_, _) => run(action);
            panel.Children.Add(button);
        }
    }

    internal void RetireSharedSkills() => SkillDeclaration.IsEnabled = SkillConsent.IsEnabled = SkillEnable.IsEnabled = false;

    private void ClearSharedSkills()
    {
        RetireSharedSkills();
        SkillSources.ItemsSource = null;
        Skills.ItemsSource = null;
    }
}
