using System.Globalization;
using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace Kora.Controls;

public sealed partial class SettingsNumberField : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<SettingsNumberField, string>(nameof(Label), string.Empty);
    public static readonly StyledProperty<string> UnitProperty =
        AvaloniaProperty.Register<SettingsNumberField, string>(nameof(Unit), string.Empty);
    public static readonly StyledProperty<int> MinimumProperty =
        AvaloniaProperty.Register<SettingsNumberField, int>(nameof(Minimum), int.MinValue);
    public static readonly StyledProperty<int> MaximumProperty =
        AvaloniaProperty.Register<SettingsNumberField, int>(nameof(Maximum), int.MaxValue);
    public static readonly StyledProperty<int?> ValueProperty =
        AvaloniaProperty.Register<SettingsNumberField, int?>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int?> SavedValueProperty =
        AvaloniaProperty.Register<SettingsNumberField, int?>(nameof(SavedValue));
    public static readonly StyledProperty<ICommand?> ActionCommandProperty =
        AvaloniaProperty.Register<SettingsNumberField, ICommand?>(nameof(ActionCommand));
    public static readonly StyledProperty<string> ActionTextProperty =
        AvaloniaProperty.Register<SettingsNumberField, string>(nameof(ActionText), "Save");
    public static readonly DirectProperty<SettingsNumberField, bool> IsInputValidProperty =
        AvaloniaProperty.RegisterDirect<SettingsNumberField, bool>(nameof(IsInputValid), field => field.IsInputValid);

    private readonly TextBox input;
    private readonly Button action;
    private readonly TextBlock unit;
    private readonly NamedTextBlock validation;
    private bool synchronizing;
    private bool publishing;
    private bool isInputValid;
    private bool hasEdited;
    private bool refreshOnAttach = true;

    public SettingsNumberField()
    {
        AvaloniaXamlLoader.Load(this);
        input = this.FindControl<TextBox>("NumberInput")!;
        action = this.FindControl<Button>("ActionButton")!;
        unit = this.FindControl<TextBlock>("UnitLabel")!;
        validation = this.FindControl<NamedTextBlock>("ValidationText")!;
        input.PropertyChanged += OnInputChanged;
        input.KeyDown += OnInputKeyDown;
    }

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public int Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public int Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public int? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public int? SavedValue { get => GetValue(SavedValueProperty); set => SetValue(SavedValueProperty, value); }
    public ICommand? ActionCommand { get => GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }
    public string ActionText { get => GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }
    public bool IsInputValid => isInputValid;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (refreshOnAttach) { SynchronizeInput(); }
        else { ValidateInput(); }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (input is null) { return; }
        if (change.Property == ValueProperty || change.Property == MinimumProperty || change.Property == MaximumProperty)
        {
            if (!publishing)
            {
                if (this.IsAttachedToVisualTree()) { SynchronizeInput(); }
                else { refreshOnAttach = true; }
            }
        }
        if (!this.IsAttachedToVisualTree()) { return; }
        if (change.Property == SavedValueProperty || change.Property == ActionCommandProperty || change.Property == UnitProperty
            || change.Property == LabelProperty || change.Property == ActionTextProperty
            || change.Property == Avalonia.Automation.AutomationProperties.NameProperty
            || change.Property == Avalonia.Automation.AutomationProperties.HelpTextProperty)
        {
            UpdatePresentation();
        }
    }

    private void SynchronizeInput()
    {
        synchronizing = true;
        try
        {
            hasEdited = false;
            input.Text = Value?.ToString(CultureInfo.CurrentCulture);
            refreshOnAttach = false;
        }
        finally { synchronizing = false; }
        ValidateInput();
    }

    private void OnInputChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (!synchronizing && this.IsAttachedToVisualTree()
            && args.Property == TextBox.TextProperty)
        {
            hasEdited = true;
            ValidateInput();
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs args)
    {
        if (!IsInputValid || Value is not { } current || args.KeyModifiers != KeyModifiers.None || args.Key is not (Key.Up or Key.Down)) { return; }
        var next = (long)current + (args.Key == Key.Up ? 1 : -1);
        if (next >= Minimum && next <= Maximum) { input.Text = next.ToString(CultureInfo.CurrentCulture); }
        args.Handled = true;
    }

    private void ValidateInput()
    {
        var valid = int.TryParse(input.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var candidate)
            && candidate >= Minimum && candidate <= Maximum;
        SetAndRaise(IsInputValidProperty, ref isInputValid, valid);
        // An incomplete edit must never become zero or allow Save to use a previous valid draft.
        if (valid && Value != candidate)
        {
            publishing = true;
            try { SetCurrentValue(ValueProperty, candidate); }
            finally { publishing = false; }
        }
        UpdatePresentation();
    }

    private void UpdatePresentation()
    {
        action.IsEnabled = IsInputValid;
        action.IsVisible = ActionCommand is not null;
        action.Classes.Set("pending", IsInputValid && SavedValue is { } saved && Value != saved);
        Avalonia.Automation.AutomationProperties.SetName(action, $"{ActionText} {Label}");
        unit.IsVisible = !string.IsNullOrEmpty(Unit);
        var accessibleLabel = Avalonia.Automation.AutomationProperties.GetName(this);
        if (string.IsNullOrWhiteSpace(accessibleLabel)) { accessibleLabel = string.IsNullOrEmpty(Unit) ? Label : $"{Label} ({Unit})"; }
        Avalonia.Automation.AutomationProperties.SetName(input, accessibleLabel);
        Avalonia.Automation.AutomationProperties.SetHelpText(input,
            $"Enter a whole number from {Minimum} to {Maximum}. {Avalonia.Automation.AutomationProperties.GetHelpText(this)}".TrimEnd());
        Avalonia.Automation.AutomationProperties.SetName(validation, $"{Label} validation");
        validation.IsVisible = hasEdited && !IsInputValid;
        validation.Text = $"Enter a whole number from {Minimum} to {Maximum}.";
    }
}
