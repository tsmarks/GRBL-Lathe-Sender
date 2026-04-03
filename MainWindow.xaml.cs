using System.Windows;
using System.Windows.Input;
using GRBL_Lathe_Control.ViewModels;

namespace GRBL_Lathe_Control;

public partial class MainWindow : Window
{
    private readonly MainViewModel? _ownedViewModel;

    public MainWindow()
        : this(new MainViewModel(), true)
    {
    }

    public MainWindow(MainViewModel viewModel)
        : this(viewModel, false)
    {
    }

    private MainWindow(MainViewModel viewModel, bool ownsViewModel)
    {
        InitializeComponent();
        _ownedViewModel = ownsViewModel ? viewModel : null;
        DataContext = viewModel;
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, System.EventArgs e)
    {
        _ownedViewModel?.Dispose();
    }

    private void OpenDualScreen_Click(object sender, RoutedEventArgs e)
    {
        ((App)Application.Current).EnterDualScreen();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.TryHandleKeyboardInput(e.Key, Keyboard.Modifiers, e.IsRepeat))
        {
            e.Handled = true;
        }
    }
}
