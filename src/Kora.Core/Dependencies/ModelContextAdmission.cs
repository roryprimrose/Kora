namespace Kora.Core.Dependencies;

/// <summary>Either a genuine host-issued local context lease or a typed rejection; never a success-shaped fallback.</summary>
public sealed class ModelContextAdmission
{
    public ModelContextAdmission(IAdmittedModelContextTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        Turn = turn;
    }

    public ModelContextAdmission(LocalContextTurnReceipt rejection)
    {
        ArgumentNullException.ThrowIfNull(rejection);
        if (rejection.Outcome == ModelTurnOutcome.Succeeded)
        {
            throw new InvalidDataException("Rejected context admission cannot describe success.");
        }
        Rejection = rejection;
    }

    public IAdmittedModelContextTurn? Turn { get; }
    public LocalContextTurnReceipt? Rejection { get; }
}