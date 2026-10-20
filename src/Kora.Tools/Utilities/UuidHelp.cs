namespace Kora.Tools.Utilities;

public sealed class UuidHelp
{
    public const string Disclaimer = "Standalone utility UUID only: not Kora identity, not registered, and not permission, grant, approval, authentication, token or secret. Format facts do not prove existence or trust.";
    public const string Syntax = "Use exactly uuid generate | uuid inspect <canonical-uuid> | uuid help. Inspection requires one nonempty lowercase D-format UUID (36 ASCII characters).";
    public string Execute() => Syntax + " " + Disclaimer
        + " Generate returns one random version 4 RFC variant UUID; no security-token or cryptographic-strength promise. No lookup, copy, save, submission or identity registration.";
}
