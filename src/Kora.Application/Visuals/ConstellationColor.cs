using System.Runtime.InteropServices;

namespace Kora.Application.Visuals;

[StructLayout(LayoutKind.Auto)]
public readonly record struct ConstellationColor(byte Red, byte Green, byte Blue);