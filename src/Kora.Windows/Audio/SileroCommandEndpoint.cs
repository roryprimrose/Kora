using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Kora.Windows.Audio;

internal sealed class SileroCommandEndpoint : IHandsFreeCommandEndpoint
{
    private readonly InferenceSession session;
    private readonly float[] input = new float[576];
    private readonly float[] state = new float[256];
    private int count;
    private int totalSamples;
    private int silenceSamples;
    private bool speech;

    public SileroCommandEndpoint(string path)
    {
        using var options = new SessionOptions
        {
            InterOpNumThreads = 1,
            IntraOpNumThreads = 1,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_FATAL,
        };
        session = new InferenceSession(path, options);
    }

    public bool Accept(ReadOnlySpan<short> audio)
    {
        foreach (var sample in audio)
        {
            input[64 + count++] = sample / 32768f;
            if (count != 512) { continue; }
            count = 0;
            totalSamples = checked(totalSamples + 512);
            using var output = session.Run(
            [
                NamedOnnxValue.CreateFromTensor("input", new DenseTensor<float>(input, [1, 576])),
                NamedOnnxValue.CreateFromTensor("state", new DenseTensor<float>(state, [2, 1, 128])),
                NamedOnnxValue.CreateFromTensor("sr", new DenseTensor<long>(new long[] { 16000 }, [])),
            ]);
            var results = output.ToArray();
            if (results.Length != 2) { throw new InvalidDataException("The admitted VAD output contract changed."); }
            var probability = results[0].AsTensor<float>();
            var nextState = results[1].AsTensor<float>();
            if (probability.Length != 1 || nextState.Length != state.Length
                || !float.IsFinite(probability.GetValue(0)) || probability.GetValue(0) is < 0 or > 1)
            {
                throw new InvalidDataException("The admitted VAD returned an invalid observation.");
            }
            for (var index = 0; index < state.Length; index++) { state[index] = nextState.GetValue(index); }
            Array.Copy(input, 512, input, 0, 64);
            if (probability.GetValue(0) >= 0.5f) { speech = true; silenceSamples = 0; }
            else { silenceSamples = checked(silenceSamples + 512); }
            if (speech ? silenceSamples >= 12800 : totalSamples >= 64000) { return true; }
        }
        return false;
    }

    public void Dispose()
    {
        Array.Clear(input);
        Array.Clear(state);
        session.Dispose();
    }
}
