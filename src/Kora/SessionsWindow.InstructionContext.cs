namespace Kora;

internal sealed partial class SessionsWindow
{
    private void BindInstructionContext(SessionsViewModel model)
    {
        RefreshInstructions.Click += async (_, _) => await (ConversationOperation = model.RefreshInstructionsAsync());
        ReviewInstruction.Click += async (_, _) => await (ConversationOperation = model.ReviewInstructionAsync());
        SubmitInstruction.Click += async (_, _) => await (ConversationOperation = model.SubmitInstructionAsync());
        ClearInstruction.Click += (_, _) => model.ClearInstructionReview();
    }
}
