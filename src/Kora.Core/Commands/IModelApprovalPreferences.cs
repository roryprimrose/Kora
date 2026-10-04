namespace Kora.Core.Commands;

public interface IModelApprovalPreferences
{
    ModelApprovalPreferences Load();

    void Save(ModelApprovalPreferences preferences);
}
