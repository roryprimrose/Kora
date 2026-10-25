using Kora.Core.Customizations;

namespace Kora.Application.Definitions;

public sealed record CustomizationAuthoringResult(string Explanation, IReadOnlyList<CustomizationFile> Files);
