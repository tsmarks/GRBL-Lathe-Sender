using System.Windows;
using System.Windows.Input;
using GRBL_Lathe_Control.ViewModels;

namespace GRBL_Lathe_Control;

public partial class MillControlScreenWindow : Window
{
    public MillControlScreenWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void ReturnToSingleScreen_Click(object sender, RoutedEventArgs e)
    {
        ((App)Application.Current).ReturnToSingleScreen();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.TryHandleKeyboardInput(e.Key, Keyboard.Modifiers, e.IsRepeat))
        {
            e.Handled = true;
        }
    }
}
