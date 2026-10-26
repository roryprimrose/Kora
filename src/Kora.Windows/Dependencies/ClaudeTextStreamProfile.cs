using Kora.Core.Providers;

namespace Kora.Windows.Dependencies;

/// <summary>Independent deployment evidence for one exact text-stream model/compute tuple and token ceilings.</summary>
public sealed record ClaudeTextStreamProfile(ProviderChoiceId Model, ProviderChoiceId Reasoning,
    ProviderChoiceId Context, int InputTokens, int OutputTokens);
