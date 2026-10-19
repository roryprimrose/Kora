using System.Runtime.InteropServices;

namespace Kora.Core.Presentation;

/// <summary>A projected presence dot in device-independent coordinates.</summary>
/// <param name="X">Horizontal centre.</param>
/// <param name="Y">Vertical centre.</param>
/// <param name="Radius">Radius, independent of speech spread.</param>
/// <param name="Depth">Opacity band, from zero to five.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct PresenceParticle(double X, double Y, double Radius, int Depth);