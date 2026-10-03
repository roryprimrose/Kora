using System.Runtime.InteropServices;

namespace Kora.Application.Visuals;

[StructLayout(LayoutKind.Auto)]
public readonly record struct ConstellationVisualFrame(
    ConstellationColor Color,
    double Opacity,
    double Scale);