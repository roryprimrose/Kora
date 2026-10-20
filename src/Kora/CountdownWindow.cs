using Avalonia.Controls;
using Avalonia.Data;
using Kora.Application.Interaction;
using Kora.Application.ViewModels;
using Kora.Core.Commands;

namespace Kora;

public sealed class CountdownWindow : Window
{
    public CountdownWindow(MainViewModel main, CountdownWorkflow workflow)
    {
        Title = "Local visual timer";
        Width = 460;
        Height = 220;
        MinWidth = 360;
        MinHeight = 200;
        ShowActivated = true;
        DataContext = workflow;
        var status = new TextBlock { Name = "CountdownStatus", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(CountdownWorkflow.Status)));
        var refresh = new Button { Name = "CountdownRefresh", Content = "Status" };
        var cancel = new Button { Name = "CountdownCancel", Content = "Cancel timer" };
        refresh.Click += (_, _) => main.ExecuteNativeCountdown(CountdownOperation.Status);
        cancel.Click += (_, _) => main.ExecuteNativeCountdown(CountdownOperation.Cancel);
        Content = new StackPanel
        {
            Margin = new(18),
            Spacing = 12,
            Children =
            {
                status,
                new TextBlock { Text = CountdownWorkflow.Scope, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, Children = { refresh, cancel } },
            },
        };
    }
}
