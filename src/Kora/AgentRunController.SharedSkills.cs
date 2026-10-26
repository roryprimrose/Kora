using Kora.Application.Agents;
using Kora.Core.Skills;

namespace Kora;

internal sealed partial class AgentRunController
{
    private void BindSharedSkillSelection(AgentRunWindow opened)
    {
        opened.SkillSources.SelectionChanged += (_, _) => { Retire(); opened.Skills.ItemsSource = null; };
        opened.Skills.SelectionChanged += (_, _) => Retire();
    }

    private bool SelectSharedSkill(AgentRunCommand command)
    {
        if (command.Action is "SkillSource")
        {
            window!.SkillSources.SelectedItem = ExactRow<SkillSourceRow>(window.SkillSources, row => row.Source.ProfileRelativeRoot, command.Argument);
            return true;
        }
        if (command.Action is "SkillSelect")
        {
            window!.Skills.SelectedItem = ExactRow<SkillRow>(window.Skills, row => row.ToString(), command.Argument);
            return true;
        }
        if (command.Action is "SkillTarget")
        {
            window!.Targets.SelectedItem = ExactRow<AgentTarget>(window.Targets,
                row => row.ToString(), command.Argument);
            return true;
        }
        return false;
    }

    private static T ExactRow<T>(Avalonia.Controls.ComboBox box, Func<T, string> name, string selection)
    {
        var matches = box.Items.OfType<T>().Where(row => string.Equals(name(row), selection, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 1 ? matches[0]
            : throw new InvalidOperationException("Select one unambiguous displayed name from the current page.");
    }

    private static void ShowSkillProfile(AgentRunWindow opened, BoundedAgentReview captured)
    {
        opened.Review.Text = $"LOCAL TEXT -> TEXT; TOOL-FREE\nCOMPLETE PROFILE\n{captured.Profile.Document.Text}\n\n"
            + $"ORIGINAL INPUT\n{captured.Input}";
        opened.SkillDeclaration.IsEnabled = true;
        opened.Status.Text = "Select the exact declared skill from its registered catalogue, then review its declaration. No reference content was read.";
    }

    private async Task<bool> RunSharedSkillActionAsync(string action, AgentRunWindow opened, Func<bool> current,
        Func<bool> admission, CancellationToken token)
    {
        if (action is "SkillSources")
        {
            Retire();
            var revision = inputRevision;
            bool Selected() => admission() && inputRevision == revision;
            var sources = await workflow.ReadSkillSourcesAsync(Selected, token);
            if (!Selected()) { return true; }
            opened.SkillSources.ItemsSource = sources.Select(source => new SkillSourceRow(source)).ToArray();
            opened.Status.Text = "Select one already registered source. This operation cannot register or write sources.";
            return true;
        }
        if (action is "SkillCatalogue")
        {
            var source = (opened.SkillSources.SelectedItem as SkillSourceRow)?.Source
                ?? throw new InvalidOperationException("Select a registered source first.");
            Retire();
            var revision = inputRevision;
            bool Selected() => admission() && inputRevision == revision;
            var catalogue = await workflow.ReadSkillCatalogueAsync(source, Selected, token);
            if (!Selected()) { return true; }
            opened.Skills.ItemsSource = catalogue.Packages.Select(snapshot => new SkillRow(source, snapshot)).ToArray();
            opened.Status.Text = "Select the exact declared skill by name and version, then review the agent profile.";
            return true;
        }
        if (action is not ("SkillDeclaration" or "SkillConsent" or "SkillEnable")) { return false; }
        var captured = review ?? throw new InvalidOperationException("Review a v3 agent profile first.");
        var skill = captured.Skill ?? throw new InvalidOperationException("This profile does not declare shared skill guidance.");
        opened.RetireSharedSkills();
        if (action is "SkillDeclaration")
        {
            var selection = opened.Skills.SelectedItem as SkillRow
                ?? throw new InvalidOperationException("Explicitly select one skill from its current catalogue.");
            await workflow.ReviewSkillDeclarationAsync(captured, selection.Source, selection.Snapshot, origin, token);
            if (!current()) { await workflow.RevokeAsync(captured); return true; }
            opened.Review.Text = $"COMPLETE PROFILE\n{captured.Profile.Document.Text}\n\nCOMPLETE PRIMARY\n{selection.Snapshot.Text}\n\n"
                + skill.DeclarationText + $"\n\nORIGINAL INPUT\n{captured.Input}";
            opened.SkillConsent.IsEnabled = true;
            opened.Status.Text = "No referenced Markdown was read. Separately consent to these exact reads; this is not enablement or invocation.";
        }
        else if (action is "SkillConsent")
        {
            await workflow.ConsentToSkillReferencesAsync(captured, origin, token);
            if (!current()) { await workflow.RevokeAsync(captured); return true; }
            ShowInvocation(opened, captured);
            opened.Review.Text += $"\n\nCOMPLETE INERT SHARED SKILL AND REFERENCES\n{skill.CompleteText}";
            opened.Confirm.IsEnabled = false;
            opened.SkillEnable.IsEnabled = true;
            opened.Status.Text = "Review every complete file, original task, target and budgets. Separately enable this single-use skill review.";
        }
        else
        {
            await workflow.EnableSkillAsync(captured, origin, token);
            if (!current()) { await workflow.RevokeAsync(captured); return true; }
            opened.Confirm.IsEnabled = captured.IsInvocationReady;
            opened.Status.Text = "Skill guidance enabled once. Separately confirm this exact local agent invocation; no skill executor is invoked.";
        }
        return true;
    }

    private sealed record SkillSourceRow(SharedSkillSource Source)
    { public override string ToString() => Source.ProfileRelativeRoot; }
    private sealed record SkillRow(SharedSkillSource Source, SharedSkillSnapshot Snapshot)
    { public override string ToString() => $"{Snapshot.Name} {Snapshot.Version}"; }
}
