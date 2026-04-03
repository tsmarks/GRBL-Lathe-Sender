using System.ComponentModel;
using System.Windows;
using GRBL_Lathe_Control.ViewModels;

namespace GRBL_Lathe_Control;

public partial class App : Application
{
    private MainWindow? _singleScreenWindow;
    private ControlScreenWindow? _controlScreenWindow;
    private DataScreenWindow? _dataScreenWindow;
    private bool _isSwitchingScreenMode;

    public MainViewModel SharedViewModel { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        SharedViewModel = new MainViewModel();
        _singleScreenWindow = new MainWindow(SharedViewModel);
        MainWindow = _singleScreenWindow;
        _singleScreenWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SharedViewModel.Dispose();
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
            _controlScreenWindow = new ControlScreenWindow(SharedViewModel);
            _dataScreenWindow = new DataScreenWindow(SharedViewModel);

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
            var replacementWindow = new MainWindow(SharedViewModel);
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
        Window? peerWindow = closingControlWindow ? _dataScreenWindow : _controlScreenWindow;

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
