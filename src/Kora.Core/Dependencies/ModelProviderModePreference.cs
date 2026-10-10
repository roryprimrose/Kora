namespace Kora.Core.Dependencies;

/// <summary>Device-local mode semantics; a preference never grants provider or egress authority.</summary>
public static class ModelProviderModePreference
{
    public const ModelProviderMode Default = ModelProviderMode.LocalOnly;
    public static IReadOnlyList<ModelProviderMode> Choices { get; } =
        Array.AsReadOnly(new[] { ModelProviderMode.LocalOnly, ModelProviderMode.LocalFirst, ModelProviderMode.HostedPreferred });

    public static bool IsValid(ModelProviderMode mode) => Choices.Contains(mode);

    public static ModelProviderMode Parse(string value) => value switch
    {
        nameof(ModelProviderMode.LocalOnly) => ModelProviderMode.LocalOnly,
        nameof(ModelProviderMode.LocalFirst) => ModelProviderMode.LocalFirst,
        nameof(ModelProviderMode.HostedPreferred) => ModelProviderMode.HostedPreferred,
        _ => throw new InvalidDataException("The saved provider mode is unknown or noncanonical."),
    };

    public static string Serialize(ModelProviderMode mode) =>
        IsValid(mode) ? mode.ToString() : throw new ArgumentOutOfRangeException(nameof(mode));
}
