namespace Kora.Application.Documentation;

public interface IUserDocumentationProvider
{
    IReadOnlyList<UserDocumentationPage> GetPages();

    UserDocumentationPage GetStartPage();
}
