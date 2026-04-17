using System.Windows;
using GRBL_Lathe_Control.Models;

namespace GRBL_Lathe_Control;

public partial class StartupSelectionWindow : Window
{
    public StartupSelectionWindow()
    {
        InitializeComponent();
    }

    public MachineMode? SelectedMode { get; private set; }

    private void LatheButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedMode = MachineMode.Lathe;
        DialogResult = true;
    }

    private void MillButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedMode = MachineMode.Mill;
        DialogResult = true;
    }
}
