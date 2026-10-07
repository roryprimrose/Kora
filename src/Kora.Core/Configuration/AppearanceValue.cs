namespace Kora.Core.Configuration;

public abstract record AppearanceValue
{
    private protected AppearanceValue() { }
    public sealed record Theme(ApplicationThemeMode Value) : AppearanceValue;
    public sealed record Number(int Value) : AppearanceValue;
    public sealed record Toggle(bool Value) : AppearanceValue;

    public ApplicationThemeMode GetTheme() => this is Theme theme
        ? theme.Value : throw new InvalidOperationException("The appearance value is not a theme.");
    public int GetNumber() => this is Number number
        ? number.Value : throw new InvalidOperationException("The appearance value is not a whole number.");
    public bool GetToggle() => this is Toggle toggle
        ? toggle.Value : throw new InvalidOperationException("The appearance value is not a boolean.");
}
