using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace SpriteEditXL.UI;

public partial class HelpDialog : Window
{
    public HelpDialog() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
