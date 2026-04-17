using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using GRBL_Lathe_Control.Models;
using GRBL_Lathe_Control.ViewModels;

namespace GRBL_Lathe_Control;

public partial class App : Application
{
    private Window? _singleScreenWindow;
    private Window? _controlScreenWindow;
    private Window? _dataScreenWindow;
    private bool _isSwitchingScreenMode;

    public MainViewModel SharedViewModel { get; private set; } = null!;

    public MachineMode CurrentMode { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var originalShutdownMode = ShutdownMode;

        if (!TryResolveMode(e.Args, out var machineMode))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var selector = new StartupSelectionWindow();
            if (selector.ShowDialog() != true || selector.SelectedMode is null)
            {
                Shutdown();
                return;
            }

            machineMode = selector.SelectedMode.Value;
        }

        ShutdownMode = originalShutdownMode;
        CurrentMode = machineMode;
        SharedViewModel = new MainViewModel(machineMode);
        _singleScreenWindow = CreateSingleScreenWindow();
        MainWindow = _singleScreenWindow;
        _singleScreenWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SharedViewModel?.Dispose();
        base.OnExit(e);
    }

    public void EnterDualScreen()
    {
        if (_isSwitchingScreenMode || _singleScreenWindow is null || _controlScreenWindow is not null || _dataScreenWindow is not null)
        {
            return;
        }

        _isSwitchingScreenMode = true;
        try
        {
            var (controlWindow, dataWindow) = CreateDualWindows();
            _controlScreenWindow = controlWindow;
            _dataScreenWindow = dataWindow;

            _controlScreenWindow.Closing += OnDualScreenWindowClosing;
            _dataScreenWindow.Closing += OnDualScreenWindowClosing;

            PositionDualWindows(_controlScreenWindow, _dataScreenWindow);
            _controlScreenWindow.Show();
            _dataScreenWindow.Show();
            MainWindow = _controlScreenWindow;
            _controlScreenWindow.Activate();

            var singleScreenWindow = _singleScreenWindow;
            _singleScreenWindow = null;
            singleScreenWindow.Close();
        }
        finally
        {
            _isSwitchingScreenMode = false;
        }
    }

    public void ReturnToSingleScreen()
    {
        if (_isSwitchingScreenMode)
        {
            return;
        }

        _isSwitchingScreenMode = true;
        try
        {
            var replacementWindow = CreateSingleScreenWindow();
            _singleScreenWindow = replacementWindow;
            MainWindow = replacementWindow;
            replacementWindow.Show();

            CloseDualWindow(_controlScreenWindow);
            CloseDualWindow(_dataScreenWindow);
            _controlScreenWindow = null;
            _dataScreenWindow = null;
            replacementWindow.WindowState = WindowState.Normal;
            replacementWindow.Activate();
        }
        finally
        {
            _isSwitchingScreenMode = false;
        }
    }

    private void OnDualScreenWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_isSwitchingScreenMode)
        {
            return;
        }

        _isSwitchingScreenMode = true;

        var closingControlWindow = ReferenceEquals(sender, _controlScreenWindow);
        var peerWindow = closingControlWindow ? _dataScreenWindow : _controlScreenWindow;

        if (closingControlWindow)
        {
            _controlScreenWindow = null;
        }
        else
        {
            _dataScreenWindow = null;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                CloseDualWindow(peerWindow);
                _controlScreenWindow = null;
                _dataScreenWindow = null;
            }
            finally
            {
                _isSwitchingScreenMode = false;
            }
        }));
    }

    private Window CreateSingleScreenWindow()
    {
        return CurrentMode == MachineMode.Lathe
            ? new MainWindow(SharedViewModel)
            : new MillMainWindow(SharedViewModel);
    }

    private (Window controlWindow, Window dataWindow) CreateDualWindows()
    {
        return CurrentMode == MachineMode.Lathe
            ? (new ControlScreenWindow(SharedViewModel), new DataScreenWindow(SharedViewModel))
            : (new MillControlScreenWindow(SharedViewModel), new MillDataScreenWindow(SharedViewModel));
    }

    private static bool TryResolveMode(string[] args, out MachineMode machineMode)
    {
        var rawMode = args.FirstOrDefault();
        if (string.Equals(rawMode, "lathe", StringComparison.OrdinalIgnoreCase))
        {
            machineMode = MachineMode.Lathe;
            return true;
        }

        if (string.Equals(rawMode, "mill", StringComparison.OrdinalIgnoreCase))
        {
            machineMode = MachineMode.Mill;
            return true;
        }

        machineMode = MachineMode.Lathe;
        return false;
    }

    private static void PositionDualWindows(Window controlWindow, Window dataWindow)
    {
        var primaryBounds = SystemParameters.WorkArea;
        PositionWindow(controlWindow, primaryBounds.Left, primaryBounds.Top, primaryBounds.Width, primaryBounds.Height);

        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualWidth = SystemParameters.VirtualScreenWidth;
        var virtualHeight = SystemParameters.VirtualScreenHeight;
        var virtualRight = virtualLeft + virtualWidth;

        var rightSideAvailable = primaryBounds.Right + (primaryBounds.Width * 0.6) <= virtualRight;
        var leftSideAvailable = virtualLeft + (primaryBounds.Width * 0.6) <= primaryBounds.Left;

        if (rightSideAvailable)
        {
            PositionWindow(dataWindow, primaryBounds.Right, virtualTop, primaryBounds.Width, virtualHeight);
            return;
        }

        if (leftSideAvailable)
        {
            PositionWindow(dataWindow, virtualLeft, virtualTop, primaryBounds.Width, virtualHeight);
            return;
        }

        dataWindow.WindowStartupLocation = WindowStartupLocation.Manual;
        dataWindow.WindowState = WindowState.Normal;
        dataWindow.Width = Math.Max(720, primaryBounds.Width * 0.42);
        dataWindow.Height = Math.Max(840, primaryBounds.Height * 0.9);
        dataWindow.Left = primaryBounds.Left + Math.Max(40, (primaryBounds.Width - dataWindow.Width) / 2);
        dataWindow.Top = primaryBounds.Top + 40;
    }

    private static void PositionWindow(Window window, double left, double top, double width, double height)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.WindowState = WindowState.Normal;
        window.Left = left;
        window.Top = top;
        window.Width = width;
        window.Height = height;
    }

    private void CloseDualWindow(Window? window)
    {
        if (window is null)
        {
            return;
        }

        window.Closing -= OnDualScreenWindowClosing;
        window.Close();
    }
}
