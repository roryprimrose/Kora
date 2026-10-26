namespace Kora.Windows.Audio;

/// <summary>Optional native recognizer observations, with no host generation or authority supplied by native text.</summary>
internal interface IActivatedHypothesisSource
{
    /// <summary>Complete hypotheses ordered within this recognizer instance.</summary>
    event EventHandler<ActivatedHypothesisEventArgs>? Hypothesized;
}
