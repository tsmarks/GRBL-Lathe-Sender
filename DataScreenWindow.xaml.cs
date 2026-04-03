using System.Windows;
using GRBL_Lathe_Control.ViewModels;

namespace GRBL_Lathe_Control;

public partial class DataScreenWindow : Window
{
    public DataScreenWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void ReturnToSingleScreen_Click(object sender, RoutedEventArgs e)
    {
        ((App)Application.Current).ReturnToSingleScreen();
    }
}
