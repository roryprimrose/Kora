namespace Kora.Application.Configuration;

/// <summary>One observed automatic preference revision with a bounded future-result lifetime.</summary>
internal sealed record AutomaticDetailPreference(long Epoch, DateTimeOffset Expires);
