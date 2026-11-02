using Kora.Application.Skills;

namespace Kora;

internal sealed partial class OwnedSkillEditorController
{
    private OwnedSkillAuthoringAuthority? initialAuthority;
    private OwnedSkillGuidanceRefinement? generatedBase;

    private void PrepareInitial(OwnedSkillEditorWindow opened, string purpose, OwnedSkillAuthoringAuthority authority)
    {
        ResetDraft(opened);
        initialAuthority = authority;
        opened.IdInput.Text = "skill-" + Guid.NewGuid().ToString("N");
        opened.VersionInput.Text = "1.0.0";
        opened.NameInput.Text = "New skill " + opened.IdInput.Text;
        opened.DescriptionInput.Text = "User-requested local task guidance";
        opened.Tool.SelectedIndex = 0;
        opened.InputFormat.SelectedIndex = 0;
        opened.Guidance.Text = "No initial guidance generated or reviewed yet.";
        opened.Critique.Text = "";
        opened.Purpose.Text = purpose;
        opened.ExpectedInput.Text = "";
        opened.ExpectedOutput.Text = "";
        validated = null;
        dirty = true;
        uncertain = false;
        opened.Report("What input will this skill receive (or explicitly none), and what complete output should it produce? "
            + "Enter those two bounded fields, then Generate initial guidance. Only the purpose and those fields may be disclosed "
            + "after separate local admission. No model has run; nothing is saved or enabled. You may rename this host-assigned draft.");
    }
}
