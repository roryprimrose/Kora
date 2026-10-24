using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Infrastructure;
using Kora.Controls;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SettingsNumberFieldTests
{
    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("1.5")]
    [InlineData("not a number")]
    [InlineData("0")]
    [InlineData("11")]
    [InlineData("2147483648")]
    [InlineData("NaN")]
    [InlineData("1e1")]
    public async Task Invalid_text_remains_visible_and_cannot_publish_or_save_an_old_draft(string text)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var draft = new NumberDraft();
            var saves = 0;
            var field = CreateField(draft, () => saves++);
            var other = new Button { Content = "Other field" };
            var window = StyledWindow(new StackPanel { Children = { field, other } });
            try
            {
                window.Show();
                window.UpdateLayout();
                var input = field.FindControl<TextBox>("NumberInput")!;
                input.Focus();
                input.Text = text;
                other.Focus();
                window.UpdateLayout();

                input.Text.Should().Be(text);
                field.IsInputValid.Should().BeFalse();
                draft.Value.Should().Be(7);
                field.FindControl<NamedTextBlock>("ValidationText")!.IsVisible.Should().BeTrue();
                var save = field.FindControl<Button>("ActionButton")!;
                save.IsEffectivelyEnabled.Should().BeFalse();
                Click(window, save);
                saves.Should().Be(0);

                input.Text = "8";
                window.UpdateLayout();
                field.IsInputValid.Should().BeTrue();
                draft.Value.Should().Be(8);
                field.FindControl<NamedTextBlock>("ValidationText")!.IsVisible.Should().BeFalse();
                Click(window, save);
                saves.Should().Be(1);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(1, 10, "1", 1)]
    [InlineData(1, 10, "10", 10)]
    [InlineData(0, 100, "0", 0)]
    [InlineData(0, 100, "100", 100)]
    [InlineData(-10, 10, "-10", -10)]
    [InlineData(-10, 10, "10", 10)]
    public async Task Valid_boundaries_publish_without_automatically_saving(int minimum, int maximum, string text, int expected)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var draft = new NumberDraft();
            var saves = 0;
            var field = CreateField(draft, () => saves++);
            field.Minimum = minimum;
            field.Maximum = maximum;
            var window = StyledWindow(field);
            try
            {
                window.Show();
                window.UpdateLayout();
                var input = field.FindControl<TextBox>("NumberInput")!;
                input.Text = text;
                window.UpdateLayout();
                field.IsInputValid.Should().BeTrue();
                draft.Value.Should().Be(expected);
                saves.Should().Be(0);

                draft.Value = 9;
                window.UpdateLayout();
                input.Text.Should().Be("9");
                field.Value.Should().Be(9);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("1.5", false)]
    [InlineData("8", false)]
    [InlineData("1.5", true)]
    public async Task Detaching_a_field_preserves_its_edit_unless_the_host_publishes_a_new_value(string text, bool hostChanges)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var draft = new NumberDraft();
            var saves = 0;
            var field = CreateField(draft, () => saves++);
            var panel = new StackPanel { Children = { field } };
            var window = StyledWindow(panel);
            try
            {
                window.Show();
                window.UpdateLayout();
                var input = field.FindControl<TextBox>("NumberInput")!;
                input.Text = text;
                panel.Children.Remove(field);
                window.UpdateLayout();
                if (hostChanges) { draft.Value = 9; }
                panel.Children.Add(field);
                window.UpdateLayout();
                input.Text.Should().Be(hostChanges ? "9" : text);
                field.IsInputValid.Should().Be(hostChanges || string.Equals(text, "8", StringComparison.Ordinal));
                field.FindControl<Button>("ActionButton")!.IsEffectivelyEnabled.Should().Be(field.IsInputValid);
                saves.Should().Be(0);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Unselected_preview_stays_null_until_an_exact_valid_number_is_entered()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var draft = new NumberDraft { Value = null };
            var saves = 0;
            var field = CreateField(draft, () => saves++);
            field.ActionText = "Preview";
            var window = StyledWindow(field);
            try
            {
                window.Show();
                window.UpdateLayout();
                field.Value.Should().BeNull();
                draft.Value.Should().BeNull();
                field.IsInputValid.Should().BeFalse();
                field.FindControl<Button>("ActionButton")!.IsEffectivelyEnabled.Should().BeFalse();
                field.FindControl<NamedTextBlock>("ValidationText")!.IsVisible.Should().BeFalse();
                field.FindControl<TextBox>("NumberInput")!.Text = "5";
                window.UpdateLayout();
                draft.Value.Should().Be(5);
                saves.Should().Be(0);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pending_emphasis_tracks_the_confirmed_value_and_preserves_host_and_command_guards(bool dark)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var draft = new NumberDraft();
            var saves = 0;
            var admitted = false;
            var command = new AsyncCommand(() => { saves++; return Task.CompletedTask; },
                exception => throw new InvalidOperationException("Unexpected save failure.", exception), () => admitted);
            var field = CreateField(draft, () => saves++);
            field.ActionCommand = command;
            var window = StyledWindow(field, dark);
            try
            {
                window.Show();
                window.UpdateLayout();
                var save = field.FindControl<Button>("ActionButton")!;
                var input = field.FindControl<TextBox>("NumberInput")!;
                save.Classes.Should().NotContain("pending");
                input.Text = "8";
                window.UpdateLayout();
                save.Classes.Should().Contain("pending");
                save.IsEffectivelyEnabled.Should().BeFalse();
                Click(window, save);
                saves.Should().Be(0);

                admitted = true;
                command.NotifyCanExecuteChanged();
                window.UpdateLayout();
                save.IsEffectivelyEnabled.Should().BeTrue();
                field.IsEnabled = false;
                window.UpdateLayout();
                save.IsEffectivelyEnabled.Should().BeFalse();
                field.IsEnabled = true;
                field.SavedValue = 8;
                window.UpdateLayout();
                save.Classes.Should().NotContain("pending");
                input.Text = "9";
                field.SavedValue = null;
                window.UpdateLayout();
                save.Classes.Should().NotContain("pending");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Keyboard_steps_are_bounded_and_save_has_a_contextual_accessible_name()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var draft = new NumberDraft { Value = 10 };
            var saves = 0;
            var field = CreateField(draft, () => saves++);
            var window = StyledWindow(field);
            try
            {
                window.Show();
                window.Activate();
                window.UpdateLayout();
                var input = field.FindControl<TextBox>("NumberInput")!;
                input.Focus(NavigationMethod.Tab).Should().BeTrue();
                ControlAutomationPeer.CreatePeerForElement(input)!.GetName().Should().Be("Keep records (days)");
                ControlAutomationPeer.CreatePeerForElement(input)!.GetHelpText().Should().Contain("1 to 10");
                window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
                window.KeyRelease(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
                draft.Value.Should().Be(10);
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                draft.Value.Should().Be(9);
                saves.Should().Be(0);
                var save = field.FindControl<Button>("ActionButton")!;
                ControlAutomationPeer.CreatePeerForElement(save)!.GetName().Should().Be("Save Keep records");
                save.Focus(NavigationMethod.Tab).Should().BeTrue();
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                saves.Should().Be(1);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Compact_row_wraps_without_clipping_and_does_not_restyle_other_windows(bool dark)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var untouched = new Button { Content = "Confirmation" };
            var outside = new Window { Content = untouched };
            var field = CreateField(new NumberDraft(), () => { });
            var window = StyledWindow(new StackPanel { Children = { field } }, dark);
            try
            {
                outside.Show();
                var originalPadding = untouched.Padding;
                window.Width = 600;
                window.Show();
                window.UpdateLayout();
                var wideHeight = field.Bounds.Height;
                wideHeight.Should().BeLessThanOrEqualTo(34);
                var input = field.FindControl<TextBox>("NumberInput")!;
                input.Bounds.Height.Should().BeInRange(30, 34);
                var save = field.FindControl<Button>("ActionButton")!;
                save.Padding.Should().Be(new Thickness(10, 4));
                save.CornerRadius.Should().Be(new CornerRadius(3));
                field.Width = 280;
                window.UpdateLayout();
                field.Bounds.Width.Should().Be(280);
                field.Bounds.Height.Should().BeGreaterThan(wideHeight + 12);
                var location = save.TranslatePoint(default, field)!.Value;
                (location.X + save.Bounds.Width).Should().BeLessThanOrEqualTo(field.Bounds.Width);
                (location.Y + save.Bounds.Height).Should().BeLessThanOrEqualTo(field.Bounds.Height);
                untouched.Padding.Should().Be(originalPadding);
            }
            finally { window.Close(); outside.Close(); }
        });
    }

    private static SettingsNumberField CreateField(NumberDraft draft, Action save)
    {
        var field = new SettingsNumberField
        {
            Label = "Keep records",
            Unit = "days",
            Minimum = 1,
            Maximum = 10,
            SavedValue = 7,
            DataContext = draft,
            ActionCommand = new AsyncCommand(() => { save(); return Task.CompletedTask; },
                exception => throw new InvalidOperationException("Unexpected save failure.", exception)),
        };
        field.Bind(SettingsNumberField.ValueProperty, new Binding(nameof(NumberDraft.Value)) { Mode = BindingMode.TwoWay });
        return field;
    }

    private static Window StyledWindow(Control content, bool dark = false)
    {
        var window = new Window { Width = 600, Height = 180, SizeToContent = SizeToContent.Height,
            Content = content, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://Kora/"))
        {
            Source = new Uri("avares://Kora/Assets/ThemeResources.axaml"),
        });
        window.Styles.Add(new StyleInclude(new Uri("avares://Kora/"))
        {
            Source = new Uri("avares://Kora/Controls/SettingsEditorStyles.axaml"),
        });
        return window;
    }

    private static void Click(Window window, Button button)
    {
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.UpdateLayout();
    }

    private sealed class NumberDraft : ObservableObject
    {
        private int? value = 7;
        public int? Value { get => value; set => SetProperty(ref this.value, value); }
    }
}
