using Kora.Core.Customizations;

namespace Kora.Application.Definitions;

public interface ICustomizationAuthoring
{
    Task<CustomizationVoiceIntent> Interpret(CustomizationVoiceContext context, string requestText,
        Func<bool> eligible, CancellationToken token);

    Task<CustomizationAuthoringResult> Refine(CustomizationSource source, IReadOnlyList<CustomizationFile> files,
        CustomizationAuthoringContext context, string requestText, Func<bool> eligible, CancellationToken token);
}
