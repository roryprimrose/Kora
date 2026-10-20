namespace Kora.Core.Configuration;

/// <summary>Direction of one bounded relative audio preference step.</summary>
public enum AudioPreferenceAdjustment
{
    /// <summary>Increase the desired preference toward its upper bound.</summary>
    Increase,

    /// <summary>Decrease the desired preference toward its lower bound.</summary>
    Decrease,
}
