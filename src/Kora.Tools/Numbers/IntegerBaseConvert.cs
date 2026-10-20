using Kora.Core.Numbers;

namespace Kora.Tools.Numbers;

public sealed class IntegerBaseConvert
{
    internal IntegerConversionResult Execute(IntegerConversionRequest request) => IntegerBasePolicy.Convert(request);
}
