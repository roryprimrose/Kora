using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record ResponseWindowCommand(
    AppearanceCommandOperation Operation, ResponseWindowOption? Option = null, bool? Value = null)
{
    public const string AlwaysShowId = "response-window.always-show";
    public const string TopmostId = "response-window.topmost";
    public const string Syntax = "list response window settings | get/status response-window.always-show or response-window.topmost | "
        + "set <exact ID> to true/false | reset <exact ID>. Spoken targets: response window always show, response window topmost. "
        + "Natural commands: keep responses visible | allow responses to hide | keep response window on top | stop keeping response window on top. "
        + "Each change affects only that device-local Boolean; no response is opened or restored.";

    private static readonly IReadOnlyDictionary<string, ResponseWindowCommand> Commands = BuildCommands();
    private static readonly string[] ReservedPrefixes =
    [
        "list response window", "get response-window", "status response-window", "set response-window", "reset response-window",
        "get response window", "status response window", "set response window", "reset response window",
        "keep responses", "allow responses", "keep response window", "stop keeping response window",
    ];

    public static IReadOnlyList<string> FixedPhrases { get; } = Commands.Keys.ToArray();

    public static ResponseWindowCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        if (!ReservedPrefixes.Any(prefix => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }
        if (input.Length > 256 || input.Any(char.IsControl))
        {
            return new(AppearanceCommandOperation.Clarify);
        }
        return Commands.TryGetValue(text, out var command) ? command : new(AppearanceCommandOperation.Clarify);
    }

    private static Dictionary<string, ResponseWindowCommand> BuildCommands()
    {
        var commands = new Dictionary<string, ResponseWindowCommand>(StringComparer.OrdinalIgnoreCase)
        {
            ["list response window settings"] = new(AppearanceCommandOperation.List),
            ["keep responses visible"] = new(AppearanceCommandOperation.Set, ResponseWindowOption.AlwaysShow, true),
            ["allow responses to hide"] = new(AppearanceCommandOperation.Set, ResponseWindowOption.AlwaysShow, false),
            ["keep response window on top"] = new(AppearanceCommandOperation.Set, ResponseWindowOption.Topmost, true),
            ["stop keeping response window on top"] = new(AppearanceCommandOperation.Set, ResponseWindowOption.Topmost, false),
        };
        AddOption(AlwaysShowId, "response window always show", ResponseWindowOption.AlwaysShow);
        AddOption(TopmostId, "response window topmost", ResponseWindowOption.Topmost);
        return commands;

        void AddOption(string id, string spokenName, ResponseWindowOption option)
        {
            foreach (var target in new[] { id, spokenName })
            {
                commands.Add("get " + target, new(AppearanceCommandOperation.Get, option));
                commands.Add("status " + target, new(AppearanceCommandOperation.Get, option));
                commands.Add("reset " + target, new(AppearanceCommandOperation.Reset, option));
                commands.Add("set " + target + " to true", new(AppearanceCommandOperation.Set, option, true));
                commands.Add("set " + target + " to false", new(AppearanceCommandOperation.Set, option, false));
            }
        }
    }
}
