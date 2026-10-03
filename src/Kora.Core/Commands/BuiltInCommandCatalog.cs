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
            Define(BuiltInAction.ShowApplication, $"show {phraseName}", $"Show the {name} window.", $"open {phraseName}", "show your window"),
            Define(BuiltInAction.HideApplication, $"hide {phraseName}", $"Hide the {name} window while listening remains enabled.", "hide your window"),
            Define(BuiltInAction.ExitApplication, $"exit {phraseName}", $"Exit {name} and release the microphone.", $"quit {phraseName}", $"close the {phraseName} application"),
            Define(BuiltInAction.RestartApplication, $"restart {phraseName}", $"Restart this {name} application.", "restart your application"),
            Define(BuiltInAction.OpenSettings, "open settings", $"Open {name} settings.", $"show {phraseName} settings"),
            Define(BuiltInAction.OpenDocumentation, "open documentation", $"Open the {name} user guide.", "show documentation", "show the user guide"),
            Define(BuiltInAction.OpenSetup, "open setup", "Open microphone and dependency readiness.", $"configure {phraseName}", "show what you need"),
            Define(BuiltInAction.ShowHelp, "what can you do", "Show supported built-in commands.", "help", "show supported commands"),
            Define(BuiltInAction.ShowVersion, "what version are you running", $"Show the running {name} version.", "show your version"),
            Define(BuiltInAction.ShowStatus, "what are you currently working on", $"Describe {name}'s current activity.", "what are you doing"),
            Define(BuiltInAction.CancelTask, "cancel task", $"Cancel the current {name} task.", "cancel current task", "stop"),
            Define(BuiltInAction.StopSpeaking, "stop speaking", $"Stop {name} speech playback."),
            Define(BuiltInAction.LockMachine, "lock the machine", "Lock the current Windows session.", "lock my computer", "lock windows"),
            Define(BuiltInAction.ProposeShutdown, "shut down the computer", "Prepare a protected shutdown proposal.", "shut down this machine", "power off the computer"),
            Define(BuiltInAction.ProposeRestart, "restart the computer", "Prepare a protected restart proposal.", "reboot this machine", "restart windows"),
            Define(BuiltInAction.CancelPowerAction, "cancel shutdown", $"Cancel {name}'s pending power proposal.", "cancel that shutdown", "cancel computer restart", "cancel that reboot"),
            Define(BuiltInAction.ShowPowerStatus, "what power action is pending", $"Describe {name}'s pending power proposal.", "are you about to restart the computer"),
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