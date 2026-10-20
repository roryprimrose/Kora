using System.Globalization;
using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record SpeechCommand(AppearanceCommandOperation Operation,
    SpeechOptionDescriptor? Descriptor = null, string? Value = null, string? Error = null)
{
    public const int MaximumLength = 320;
    private static readonly string[] Prefixes = ["list speech", "get speech", "set speech", "reset speech"];
    private static readonly string[] SummaryPrefixes = ["show spoken summary", "set spoken summary", "reset spoken summary"];
    private static readonly (SpeechOption Option, string Target)[] SummaryTargets =
    [
        (SpeechOption.SummarySentences, "spoken summary sentence limit"),
        (SpeechOption.SummaryWords, "spoken summary word limit"),
    ];
    public const string Syntax = "Use list speech settings, get <speech.id>, set <speech.id> to <exact installed ID or integer cap>, or reset <speech.id>. Listed spoken names also work. Voice choices accept provider / ID, or an unambiguous exact voice ID, and explicitly select that provider too. Reset provider restores Windows/default voice; reset voice restores that provider's advertised default. speech.summary-sentences accepts 1-3; speech.summary-words accepts 1-80. Or use show spoken summary limits, set spoken summary sentence limit to <integer>, set spoken summary word limit to <integer>, reset spoken summary sentence limit, or reset spoken summary word limit. Natural integers use canonical decimal digits, without signs, padding or leading zeroes. Reset a cap preserves the other cap. These are future ordinary-speech budgets, not a request to speak now. Ordinary results exceeding either cap remain fully visual, not truncated or regenerated.";
    public static IReadOnlyList<string> FixedPhrases { get; } = Array.AsReadOnly(
        new[] { "show spoken summary limits" }.Concat(SummaryTargets.SelectMany(target =>
            new[] { "reset " + target.Target }.Concat(Enumerable.Range(1,
                target.Option == SpeechOption.SummarySentences ? SpokenSummaryLimits.MaximumSentences : SpokenSummaryLimits.MaximumWords)
                .Select(value => "set " + target.Target + " to " + value.ToString(CultureInfo.InvariantCulture))))).ToArray());
    public bool IsNaturalSummary { get; private init; }

    public static SpeechCommand? Parse(string transcript, string assistantName)
    {
        var text = transcript.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        var naturalSummary = SummaryPrefixes.Any(prefix => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (!naturalSummary && !Prefixes.Any(prefix => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) { return null; }
        if (text.Length > MaximumLength) { return new(AppearanceCommandOperation.Clarify, Error: "Speech command is too long. " + Syntax) { IsNaturalSummary = naturalSummary }; }
        if (naturalSummary) { return ParseSummary(text, transcript) with { IsNaturalSummary = true }; }
        if (text.Equals("list speech settings", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.List); }
        var separator = text.IndexOf(' ', StringComparison.Ordinal);
        var operation = text[..separator];
        var target = text[(separator + 1)..].Trim();
        string? value = null;
        var valueSeparator = target.IndexOf(" to ", StringComparison.OrdinalIgnoreCase);
        if (operation.Equals("set", StringComparison.OrdinalIgnoreCase) && valueSeparator >= 0)
        {
            value = target[(valueSeparator + 4)..];
            target = target[..valueSeparator];
        }
        var descriptor = SpeechOptionRegistry.Options.FirstOrDefault(item =>
            item.Id.Equals(target, StringComparison.OrdinalIgnoreCase)
            || item.SpokenName.Equals(target, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null) { return new(AppearanceCommandOperation.Clarify, Error: Syntax); }
        if (operation.Equals("get", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Get, descriptor); }
        if (operation.Equals("reset", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Reset, descriptor); }
        if (string.IsNullOrWhiteSpace(value)) { return new(AppearanceCommandOperation.Clarify, descriptor, Error: Syntax); }
        return new(AppearanceCommandOperation.Set, descriptor, value);
    }

    private static SpeechCommand ParseSummary(string text, string transcript)
    {
        if (transcript.Any(char.IsControl)) { return new(AppearanceCommandOperation.Clarify, Error: Syntax); }
        if (text.Equals("show spoken summary limits", StringComparison.OrdinalIgnoreCase))
        {
            return new(AppearanceCommandOperation.Get, SpeechOptionRegistry.Get(SpeechOption.SummarySentences));
        }
        foreach (var target in SummaryTargets)
        {
            var descriptor = SpeechOptionRegistry.Get(target.Option);
            if (text.Equals("reset " + target.Target, StringComparison.OrdinalIgnoreCase))
            {
                return new(AppearanceCommandOperation.Reset, descriptor);
            }
            var prefix = "set " + target.Target + " to ";
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { continue; }
            var value = text[prefix.Length..];
            try
            {
                var limits = SpokenSummaryLimits.Default.With(target.Option, value);
                var canonical = (target.Option == SpeechOption.SummarySentences ? limits.Sentences : limits.Words)
                    .ToString(CultureInfo.InvariantCulture);
                if (value.Equals(canonical, StringComparison.Ordinal))
                {
                    return new(AppearanceCommandOperation.Set, descriptor, value);
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                return new(AppearanceCommandOperation.Clarify, descriptor, Error: Syntax);
            }
        }
        return new(AppearanceCommandOperation.Clarify, Error: Syntax);
    }
}
