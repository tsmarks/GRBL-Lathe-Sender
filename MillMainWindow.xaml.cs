using System.Windows;
using System.Windows.Input;
using GRBL_Lathe_Control.Models;
using GRBL_Lathe_Control.ViewModels;

namespace GRBL_Lathe_Control;

public partial class MillMainWindow : Window
{
    private readonly MainViewModel? _ownedViewModel;

    public MillMainWindow()
        : this(new MainViewModel(MachineMode.Mill), true)
    {
    }

    public MillMainWindow(MainViewModel viewModel)
        : this(viewModel, false)
    {
    }

    private MillMainWindow(MainViewModel viewModel, bool ownsViewModel)
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
