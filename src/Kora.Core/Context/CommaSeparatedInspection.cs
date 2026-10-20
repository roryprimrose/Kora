namespace Kora.Core.Context;

/// <summary>Counts only; failure locations are zero-based UTF-16 offsets and one-based logical records.</summary>
public sealed record CommaSeparatedInspection(
    CommaSeparatedSyntax Syntax, int RecordCount, int MinimumFields, int MaximumFields,
    int? ErrorOffset = null, int? ErrorRecord = null)
{
    public bool IsRagged => MinimumFields != MaximumFields;
}
