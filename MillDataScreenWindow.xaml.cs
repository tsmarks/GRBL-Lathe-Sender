using System.Windows;
using System.Windows.Input;
using GRBL_Lathe_Control.ViewModels;

namespace GRBL_Lathe_Control;

public partial class MillDataScreenWindow : Window
{
    private const double DesignWidth = 1392;
    private const double DesignHeight = 760;

    public MillDataScreenWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => UpdateResponsiveScale();
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

    private void ResponsiveHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveScale();
    }

    private void UpdateResponsiveScale()
    {
        ResponsiveLayoutHelper.UpdateScale(ResponsiveHost, ResponsiveScaleTransform, DesignWidth, DesignHeight);
    }
}
