namespace Kora.Core.Memory;

public enum MemoryContentClass
{
    Unknown, ExplicitFact, ResponsePreference, WorkflowPreference, Decision,
    Credential, Secret, Health, InferredTrait, TransientTask, ModelClaim,
}
