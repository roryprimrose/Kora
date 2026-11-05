namespace Kora.Core.Dependencies;

/// <summary>Content-free host observation of one actual model turn, retained with its session.</summary>
public sealed record SessionModelUsage(
    ModelTurnProvenance Provenance, string Model, ModelTokenUsage? TokenUsage, TimeSpan Elapsed, DateTimeOffset ObservedAt)
{
    public void Validate()
    {
        if (Provenance is null || Provenance.Request is null || Provenance.Selection is null
            || !Provenance.Selection.IsValid || Provenance.Turn.Value == Guid.Empty
            || string.IsNullOrWhiteSpace(Model) || Model.Length > 256 || Model.Any(char.IsControl)
            || Elapsed < TimeSpan.Zero || ObservedAt == default)
        { throw new InvalidDataException("The session model usage observation is invalid."); }
        Provenance.Request.RequestId.Validate();
        Provenance.Request.SessionId.Validate();
        Provenance.Request.TaskId.Validate();
        Provenance.Request.InvocationId?.Validate();
        TokenUsage?.Validate();
    }
}
