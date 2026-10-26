namespace Kora.Application.Dependencies;

/// <summary>Canonical host resource identity, not a provider-supplied name or endpoint alias.</summary>
internal readonly record struct RuntimeResourceId(Guid Value);