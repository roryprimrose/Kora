namespace W2Payload;

public static class FixedEffect
{
    public static string TokenJson() => System.Text.Json.JsonSerializer.Serialize(
        ContainmentProof.Native.Token(), ContainmentProof.Wire.Json);

    public static string ImagesJson() => System.Text.Json.JsonSerializer.Serialize(
        W2Proof.Images.Capture(), ContainmentProof.Wire.Json);

    public static int Observe() =>
#if UNDECLARED
        22;
#else
        11;
#endif
}
