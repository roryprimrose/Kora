namespace Kora;

internal sealed partial class EvidenceWindow
{
    private void BindExport(EvidenceViewModel model)
    {
        ExportPage.Click += async (_, _) => await model.ExportPageAsync(this);
    }
}
