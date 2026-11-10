using Avalonia.Controls;

namespace Kora;

internal sealed class AgentRunWindow : Window
{
    internal AgentRunView View { get; }
    internal TextBox Source => View.Source;
    internal TextBox Input => View.Input;
    internal TextBox Recommendation => View.Recommendation;
    internal ComboBox RecommendedSources => View.RecommendedSources;
    internal ComboBox Targets => View.Targets;
    internal TextBox Review => View.Review;
    internal TextBox Result => View.Result;
    internal Button Confirm => View.Confirm;
    internal Button Resolve => View.Resolve;
    internal TextBlock Status => View.Status;
    internal ComboBox SkillSources => View.SkillSources;
    internal ComboBox Skills => View.Skills;
    internal Button SkillDeclaration => View.SkillDeclaration;
    internal Button SkillConsent => View.SkillConsent;
    internal Button SkillEnable => View.SkillEnable;
    internal AgentRunWindow(Action<string> run)
    {
        Title = "One reviewed local bounded agent";
        Width = 850;
        Height = 950;
        View = new(this, run, static () => true);
        Content = View;
        Closed += (_, _) => View.Dispose();
    }
    internal void Clear() => View.Clear();
    internal void RetireSharedSkills() => View.RetireSharedSkills();
}
