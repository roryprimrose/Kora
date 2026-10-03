namespace Kora.Core.Commands;

public sealed class BuiltInCommandCatalog
{
    private static readonly IReadOnlyList<CommandDefinition> Commands =
    [
        Define(BuiltInAction.ShowApplication, "show kora", "Show the Kora window.", "open kora", "show your window"),
        Define(BuiltInAction.HideApplication, "hide kora", "Hide the Kora window while listening remains enabled.", "hide your window"),
        Define(BuiltInAction.ExitApplication, "exit kora", "Exit Kora and release the microphone.", "quit kora", "close the kora application"),
        Define(BuiltInAction.RestartApplication, "restart kora", "Restart this Kora application.", "restart your application"),
        Define(BuiltInAction.OpenSettings, "open settings", "Open Kora settings.", "show kora settings"),
        Define(BuiltInAction.OpenSetup, "open setup", "Open microphone and dependency readiness.", "configure kora", "show what you need"),
        Define(BuiltInAction.ShowHelp, "what can you do", "Show supported built-in commands.", "help", "show supported commands"),
        Define(BuiltInAction.ShowVersion, "what version are you running", "Show the running Kora version.", "show your version"),
        Define(BuiltInAction.ShowStatus, "what are you currently working on", "Describe Kora's current activity.", "what are you doing"),
        Define(BuiltInAction.CancelTask, "cancel task", "Cancel the current Kora task.", "cancel current task", "stop"),
        Define(BuiltInAction.StopSpeaking, "stop speaking", "Stop Kora speech playback."),
        Define(BuiltInAction.LockMachine, "lock the machine", "Lock the current Windows session.", "lock my computer", "lock windows"),
        Define(BuiltInAction.ProposeShutdown, "shut down the computer", "Prepare a protected shutdown proposal.", "shut down this machine", "power off the computer"),
        Define(BuiltInAction.ProposeRestart, "restart the computer", "Prepare a protected restart proposal.", "reboot this machine", "restart windows"),
        Define(BuiltInAction.CancelPowerAction, "cancel shutdown", "Cancel Kora's pending power proposal.", "cancel that shutdown", "cancel computer restart", "cancel that reboot"),
        Define(BuiltInAction.ShowPowerStatus, "what power action is pending", "Describe Kora's pending power proposal.", "are you about to restart the computer"),
    ];

    public IReadOnlyList<CommandDefinition> GetCommands() => Commands;

    private static CommandDefinition Define(
        BuiltInAction action,
        string canonicalPhrase,
        string description,
        params string[] aliases) =>
        new(action, canonicalPhrase, description, aliases);
}