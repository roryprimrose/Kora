using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Kora;

// Only native layout changes: never write Text, selection, focus or the undo stack.
internal sealed class NativeDraftWrapping : IDisposable
{
    private readonly Window window;
    private readonly TextBox editor;
    private readonly CheckBox choice;
    private readonly INotifyPropertyChanged model;
    private readonly Func<Func<bool>> captureSource;
    private readonly TextWrapping initialWrapping;
    private Func<bool> sourceLive;
    private long generation;
    private bool resetting;
    private bool disposed;

    internal NativeDraftWrapping(Window window, TextBox editor, CheckBox choice,
        INotifyPropertyChanged model, Func<Func<bool>> captureSource)
    {
        this.window = window;
        this.editor = editor;
        this.choice = choice;
        this.model = model;
        this.captureSource = captureSource;
        initialWrapping = editor.TextWrapping;
        sourceLive = captureSource();
        Reset();
        choice.IsCheckedChanged += Changed;
        model.PropertyChanged += SourceChanged;
        window.PropertyChanged += WindowChanged;
        window.Closed += Closed;
        editor.PropertyChanged += EditorChanged;
        editor.DetachedFromVisualTree += Detached;
        choice.DetachedFromVisualTree += Detached;
    }

    private void Changed(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (resetting || disposed) { return; }
        if (!window.IsVisible || !editor.IsEnabled || !sourceLive()
            || !ReferenceEquals(TopLevel.GetTopLevel(editor), window)
            || !ReferenceEquals(TopLevel.GetTopLevel(choice), window))
        {
            Reset();
            return;
        }
        Apply(choice.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap);
    }

    private void Apply(TextWrapping wrapping)
    {
        var current = generation;
        editor.TextWrapping = wrapping;
        // A synchronous native property observer may retire this source/window.
        if (generation != current) { return; }
        ScrollViewer.SetHorizontalScrollBarVisibility(editor,
            wrapping == TextWrapping.NoWrap ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled);
    }

    private void Reset()
    {
        generation++;
        resetting = true;
        try
        {
            choice.IsChecked = initialWrapping != TextWrapping.NoWrap;
            Apply(initialWrapping);
        }
        finally { resetting = false; }
    }

    private void SourceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (disposed || sourceLive()) { return; }
        sourceLive = captureSource();
        Reset();
    }

    private void WindowChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == StyledElement.DataContextProperty && !ReferenceEquals(window.DataContext, model))
        {
            Dispose();
        }
        else if (args.Property == Visual.IsVisibleProperty && !window.IsVisible) { Reset(); }
    }

    private void Closed(object? sender, EventArgs args) => Dispose();

    private void EditorChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == StyledElement.DataContextProperty && !ReferenceEquals(editor.DataContext, model))
        { Dispose(); }
    }

    private void Detached(object? sender, VisualTreeAttachmentEventArgs args) => Dispose();

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        choice.IsCheckedChanged -= Changed;
        model.PropertyChanged -= SourceChanged;
        window.PropertyChanged -= WindowChanged;
        window.Closed -= Closed;
        editor.PropertyChanged -= EditorChanged;
        editor.DetachedFromVisualTree -= Detached;
        choice.DetachedFromVisualTree -= Detached;
        Reset();
    }
}
