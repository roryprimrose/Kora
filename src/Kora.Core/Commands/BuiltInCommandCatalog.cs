using Kora.Core.Configuration;

namespace Kora.Core.Commands;

public sealed class BuiltInCommandCatalog
{
    public IReadOnlyList<CommandDefinition> GetCommands() =>
        GetCommands(AssistantNameRules.DefaultName);

    public IReadOnlyList<CommandDefinition> GetCommands(string assistantName)
    {
        var name = AssistantNameRules.Normalize(assistantName);
        var phraseName = name.ToLowerInvariant();
        CommandDefinition[] commands =
        [
            Define(BuiltInAction.ShowApplication, $"show {phraseName}", $"Show the {name} window.", $"open {phraseName}", "show your window", "open your window", "show yourself", $"bring up {phraseName}"),
            Define(BuiltInAction.HideApplication, $"hide {phraseName}", $"Hide the {name} window while listening remains enabled.", "hide your window", "hide yourself", "close your window", $"hide the {phraseName} window"),
            Define(BuiltInAction.ExitApplication, $"exit {phraseName}", $"Exit {name} and release the microphone.", $"quit {phraseName}", $"close the {phraseName} application", $"close {phraseName}", "exit the application", "quit the application"),
            Define(BuiltInAction.RestartApplication, $"restart {phraseName}", $"Restart this {name} application.", "restart your application", "restart the app", "restart yourself", $"relaunch {phraseName}"),
            Define(BuiltInAction.OpenSettings, "open settings", $"Open {name} settings.", $"show {phraseName} settings", "show settings", "open preferences", "change settings", "settings"),
            Define(BuiltInAction.OpenDocumentation, "open documentation", $"Open the {name} user guide.", "show documentation", "show the user guide", "open the user guide", "open the manual", "show me the instructions"),
            Define(BuiltInAction.OpenSetup, "open setup", "Open microphone and dependency readiness.", $"configure {phraseName}", "show what you need", "set up local models", "check setup", "show readiness", "check dependencies", "review local model setup"),
            Define(BuiltInAction.ShowHelp, "what can you do", "Show supported built-in commands.", "help", "show supported commands", "list commands", "what commands do you know", "what can i say", "show me what you can do"),
            Define(BuiltInAction.ShowVersion, "what version are you running", $"Show the running {name} version.", "show your version", "what version is this", "tell me your version", $"which version of {phraseName} is this"),
            Define(BuiltInAction.ShowStatus, "what are you currently working on", $"Describe {name}'s current activity and setup queue.", "what are you doing", "what do you have left to do", "what are you working on", "what tasks are left", "show the task queue", "show setup status", "what is your status"),
            Define(BuiltInAction.ShowCurrentTaskProgress, "what is the current task status", $"Report the current {name} setup task's state and observed progress.", "what is the current task progress", "how far along is the current task", "show task progress", "show current task status", "how is the current task going", "what's the progress of the current task", "how much of the current task is done", "how is setup progressing"),
            Define(BuiltInAction.CancelTask, "cancel task", $"Cancel the current {name} task.", "cancel current task", "stop", "cancel the current task", "stop the current task", "stop current task", "cancel the download", "stop generating"),
            Define(BuiltInAction.StopSpeaking, "stop speaking", $"Stop {name} speech playback.", "stop talking", "be quiet", "stop reading aloud", "stop the voice"),
            Define(BuiltInAction.LockMachine, "lock the machine", "Lock the current Windows session.", "lock my computer", "lock windows", "lock this computer", "lock my pc", "lock my screen", "lock this workstation"),
            Define(BuiltInAction.ProposeShutdown, "shut down the computer", "Prepare a protected shutdown proposal.", "shut down this machine", "power off the computer", "turn off my computer", "shut down my pc", "power down this computer"),
            Define(BuiltInAction.ProposeRestart, "restart the computer", "Prepare a protected restart proposal.", "reboot this machine", "restart windows", "reboot my computer", "restart my pc", "reboot the computer"),
            Define(BuiltInAction.CancelPowerAction, "cancel shutdown", $"Cancel {name}'s pending power proposal.", "cancel that shutdown", "cancel computer restart", "cancel that reboot", "don't shut down the computer", "abort shutdown", "cancel the reboot", "abort restart"),
            Define(BuiltInAction.ShowPowerStatus, "what power action is pending", $"Describe {name}'s pending power proposal.", "are you about to restart the computer", "is a shutdown pending", "is a restart pending", "show pending power action"),
            Define(BuiltInAction.ListGrants, "list grants", $"Open the current {name} model-action grants.", "show grants", "view grants", "what grants are active", "list my approvals", "show my permissions"),
            Define(BuiltInAction.ManageGrants, "manage grants", $"Choose an action and prepare a grant change in {name}.", "add a grant", "edit a grant", "remove a grant", "change grants", "manage approvals"),
            Define(BuiltInAction.ShowModelExecution, "which models are enabled", $"Show whether {name} may use local and hosted models.", "show model settings", "show model configuration", "what models can you use", "are local models enabled", "are hosted models enabled"),
            Define(BuiltInAction.EnableLocalModels, "enable local models", $"Allow {name} to use ready local models.", "turn on local models", "use local models", "allow local models"),
            Define(BuiltInAction.DisableLocalModels, "disable local models", $"Stop {name} from using local models.", "turn off local models", "stop using local models", "block local models"),
            Define(BuiltInAction.EnableHostedModels, "enable hosted models", $"Allow {name} to use configured hosted model providers.", "turn on hosted models", "use hosted models", "allow hosted models", "enable cloud models"),
            Define(BuiltInAction.DisableHostedModels, "disable hosted models", $"Stop {name} from using hosted model providers.", "turn off hosted models", "stop using hosted models", "block hosted models", "disable cloud models"),
        ];

        var conflict = commands
            .SelectMany(command => command.AllPhrases.Select(phrase => (
                Phrase: BuiltInCommandRouter.Normalize(phrase),
                command.Action)))
            .GroupBy(item => item.Phrase, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Select(item => item.Action).Distinct().Skip(1).Any());
        if (conflict is not null)
        {
            throw new ArgumentException(
                $"The assistant name conflicts with the built-in command phrase “{conflict.Key}”.",
                nameof(assistantName));
        }

        return commands;
    }

    private static CommandDefinition Define(
        BuiltInAction action,
        string canonicalPhrase,
        string description,
        params string[] aliases) =>
        new(action, canonicalPhrase, description, aliases);
}