namespace Kora;

internal sealed partial class EvidenceView
{
    private void BindExport(EvidenceViewModel model)
    {
        ExportPage.Click += async (_, _) => await model.ExportPageAsync(owner);
    }
}
