using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record AppearanceOptionState(AppearanceOptionDescriptor Descriptor, AppearanceValue Value, long Revision, bool IsSaved);
