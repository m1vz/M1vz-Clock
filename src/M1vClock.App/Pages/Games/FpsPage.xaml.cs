using System.ComponentModel;
using System.Windows;

namespace M1vClock.App.Pages.Games;

public partial class FpsPage : System.Windows.Controls.UserControl
{
    public FpsPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged old)
        {
            old.PropertyChanged -= OnPageChanged;
        }

        if (e.NewValue is INotifyPropertyChanged current)
        {
            current.PropertyChanged += OnPageChanged;
        }

        KeyBox.Clear();
    }

    private void OnPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FpsPageViewModel.KeyText) && sender is FpsPageViewModel { KeyText.Length: 0 } && KeyBox.Password.Length > 0)
        {
            KeyBox.Clear();
        }
    }

    private void OnKeyChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is FpsPageViewModel page)
        {
            page.KeyText = KeyBox.Password;
        }
    }
}
