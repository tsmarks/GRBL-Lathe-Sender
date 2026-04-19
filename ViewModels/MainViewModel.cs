using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using GRBL_Lathe_Control.Infrastructure;
using GRBL_Lathe_Control.Models;
using GRBL_Lathe_Control.Services;
using Microsoft.Win32;

namespace GRBL_Lathe_Control.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly GrblClient _grblClient = new();
    private readonly MachineMode _machineMode;

    private CancellationTokenSource? _programCancellation;
    private GCodeProgram? _loadedProgram;

    private string _selectedPort = string.Empty;
    private string _baudRateInput = "115200";
    private string _connectionStatus = "Disconnected";
    private string _controllerState = "Offline";
    private string _workXInput = "0";
    private string _workYInput = "0";
    private string _workZInput = "0";
    private string _diameterTouchOffInput = "0";
    private string _goToXInput = "1";
    private string _goToYInput = "1";
    private string _goToZInput = "0";
    private string _newToolNumberInput = string.Empty;
    private string _xJogFeedInput = "200";
    private string _yJogFeedInput = "200";
    private string _zJogFeedInput = "400";
    private string _aJogFeedInput = "200";
    private string _bJogFeedInput = "200";
    private string _toolChangeXInput = "0";
    private string _toolChangeYInput = "0";
    private string _toolChangeSafeZInput = "0";
    private string _probeStartZInput = "0";
    private string _probeTravelInput = "50";
    private string _probeFeedInput = "100";
    private string _probeFineFeedInput = "25";
    private string _probeRetractInput = "2";
    private string _spindleMaxSpeedInput = "1000";
    private string _selectedWorkCoordinateSystem = "G54";
    private string _programPath = "No file loaded";
    private double _machineX;
    private double _machineY;
    private double _machineZ;
    private double _machineA;
    private double _machineB;
    private double _workX;
    private double _workY;
    private double _workZ;
    private double _workA;
    private double _workB;
    private bool _xLimitPinHigh;
    private bool _yLimitPinHigh;
    private bool _zLimitPinHigh;
    private bool _probePinHigh;
    private bool _isConnected;
    private bool _isProgramRunning;
    private bool _isProgramPaused;
    private double _selectedXJogStep = 0.1;
    private double _selectedYJogStep = 0.1;
    private double _selectedZJogStep = 1;
    private double _selectedAJogStep = 1;
    private double _selectedBJogStep = 1;
    private int _spindleMaxSpeed = 1000;
    private int _selectedSpindleSpeed = 500;
    private int _feedOverridePercent = 100;
    private int _lastKnownFeedOverridePercent = 100;
    private bool _isUpdatingFeedOverrideFromController;
    private int _executedProgramLines;
    private double _programProgressPercent;
    private IReadOnlyList<ToolPathSegment> _toolPathSegments = Array.Empty<ToolPathSegment>();
    private CancellationTokenSource? _feedOverrideAdjustmentCancellation;
    private bool _isKeyboardControlEnabled;
    private bool _isToolOffsetsLocked = true;
    private bool _suppressMachineSettingsPersistence;
    private bool _suppressToolOffsetPersistence;
    private int _activeToolNumber;
    private char _keyboardJogAxis = 'X';
    private string _lastAppliedWorkCoordinateSystem = string.Empty;
    private double? _millProbeReferenceWorkZ;
    private double? _millLastProbeWorkZ;

    public MainViewModel(MachineMode machineMode = MachineMode.Lathe)
    {
        _dispatcher = Application.Current.Dispatcher;
        _machineMode = machineMode;

        RefreshPortsCommand = new RelayCommand(RefreshPorts);
        AddToolCommand = new RelayCommand(AddTool, CanAddTool);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, CanConnect);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected);
        ZeroXCommand = new AsyncRelayCommand(() => SetWorkCoordinateAsync(xValue: 0), CanOffsetOrJog);
        ZeroYCommand = new AsyncRelayCommand(() => SetWorkCoordinateAsync(yValue: 0), CanOffsetOrJog);
        ZeroZCommand = new AsyncRelayCommand(() => SetWorkCoordinateAsync(zValue: 0), CanOffsetOrJog);
        ZeroAllCommand = new AsyncRelayCommand(() => SetWorkCoordinateAsync(xValue: 0, zValue: 0), CanOffsetOrJog);
        HomeCommand = new AsyncRelayCommand(HomeAsync, CanOffsetOrJog);
        SoftResetCommand = new AsyncRelayCommand(SoftResetAsync, () => IsConnected);
        UnlockCommand = new AsyncRelayCommand(UnlockAsync, () => IsConnected);
        SetSpindleMaxSpeedCommand = new RelayCommand(SetSpindleMaxSpeed, CanSetSpindleMaxSpeed);
        ApplySpindleSpeedCommand = new AsyncRelayCommand(ApplySpindleSpeedAsync, CanAdjustManualSpindle);
        StopSpindleCommand = new AsyncRelayCommand(StopSpindleAsync, CanAdjustManualSpindle);
        SetWorkXCommand = new AsyncRelayCommand(SetWorkXAsync, CanOffsetOrJog);
        SetWorkYCommand = new AsyncRelayCommand(SetWorkYAsync, CanOffsetOrJog);
        SetWorkZCommand = new AsyncRelayCommand(SetWorkZAsync, CanOffsetOrJog);
        SetXFromDiameterCommand = new AsyncRelayCommand(SetXFromDiameterAsync, CanOffsetOrJog);
        GoToXCommand = new AsyncRelayCommand(GoToXAsync, CanOffsetOrJog);
        GoToYCommand = new AsyncRelayCommand(GoToYAsync, CanOffsetOrJog);
        GoToZCommand = new AsyncRelayCommand(GoToZAsync, CanOffsetOrJog);
        GoToRadiusPlusOneCommand = new AsyncRelayCommand(GoToRadiusPlusOneAsync, CanOffsetOrJog);
        JogXPositiveCommand = new AsyncRelayCommand(() => JogAxisAsync("X", SelectedXJogStep, XJogFeedInput), CanOffsetOrJog);
        JogXNegativeCommand = new AsyncRelayCommand(() => JogAxisAsync("X", -SelectedXJogStep, XJogFeedInput), CanOffsetOrJog);
        JogYPositiveCommand = new AsyncRelayCommand(() => JogAxisAsync("Y", SelectedYJogStep, YJogFeedInput), CanOffsetOrJog);
        JogYNegativeCommand = new AsyncRelayCommand(() => JogAxisAsync("Y", -SelectedYJogStep, YJogFeedInput), CanOffsetOrJog);
        JogZPositiveCommand = new AsyncRelayCommand(() => JogAxisAsync("Z", SelectedZJogStep, ZJogFeedInput), CanOffsetOrJog);
        JogZNegativeCommand = new AsyncRelayCommand(() => JogAxisAsync("Z", -SelectedZJogStep, ZJogFeedInput), CanOffsetOrJog);
        JogAPositiveCommand = new AsyncRelayCommand(() => JogAxisAsync("A", SelectedAJogStep, AJogFeedInput), CanOffsetOrJog);
        JogANegativeCommand = new AsyncRelayCommand(() => JogAxisAsync("A", -SelectedAJogStep, AJogFeedInput), CanOffsetOrJog);
        JogBPositiveCommand = new AsyncRelayCommand(() => JogAxisAsync("B", SelectedBJogStep, BJogFeedInput), CanOffsetOrJog);
        JogBNegativeCommand = new AsyncRelayCommand(() => JogAxisAsync("B", -SelectedBJogStep, BJogFeedInput), CanOffsetOrJog);
        CaptureToolChangePositionCommand = new RelayCommand(CaptureToolChangePosition, () => IsMillMode && IsConnected);
        CaptureProbeStartZCommand = new RelayCommand(CaptureProbeStartZ, () => IsMillMode && IsConnected);
        GoToToolChangeCommand = new AsyncRelayCommand(GoToToolChangeAsync, () => IsMillMode && CanOffsetOrJog());
        GoSafeZCommand = new AsyncRelayCommand(GoSafeZAsync, () => IsMillMode && CanOffsetOrJog());
        GoToXYZeroCommand = new AsyncRelayCommand(GoToXYZeroAsync, () => IsMillMode && CanOffsetOrJog());
        CalibrateToolProbePlateCommand = new AsyncRelayCommand(CalibrateToolProbePlateAsync, () => IsMillMode && CanOffsetOrJog());
        RunToolProbeCommand = new AsyncRelayCommand(RunToolProbeAsync, () => IsMillMode && CanOffsetOrJog());
        LoadProgramCommand = new RelayCommand(LoadProgram, () => !IsProgramRunning);
        StartProgramCommand = new AsyncRelayCommand(StartProgramAsync, CanStartProgram);
        PauseResumeProgramCommand = new AsyncRelayCommand(PauseResumeProgramAsync, CanPauseProgram);
        StopProgramCommand = new AsyncRelayCommand(StopProgramAsync, CanStopProgram);

        _grblClient.StatusReceived += OnGrblStatusReceived;
        _grblClient.MessageReceived += OnGrblMessageReceived;

        LoadPersistedMachineSettings();

        if (IsLatheMode)
        {
            LoadPersistedToolOffsets();
            EnsureMasterToolEntry();
        }

        RefreshPorts();
    }

    public ObservableCollection<string> AvailablePorts { get; } = new();

    public ObservableCollection<string> ControllerLog { get; } = new();

    public ObservableCollection<ToolOffsetEntryViewModel> ToolOffsets { get; } = new();

    public IReadOnlyList<double> XJogSteps { get; } = [0.01, 0.05, 0.1, 0.5, 1, 5, 10];

    public IReadOnlyList<double> ZJogSteps { get; } = [0.01, 0.05, 0.1, 0.5, 1, 5, 10, 50, 100];

    public IReadOnlyList<double> LinearJogSteps { get; } = [0.01, 0.05, 0.1, 0.5, 1, 5, 10, 50, 100];

    public IReadOnlyList<double> RotaryJogSteps { get; } = [0.1, 0.5, 1, 5, 10, 45, 90];

    public IReadOnlyList<string> WorkCoordinateSystems { get; } = ["G54", "G55", "G56", "G57", "G58", "G59"];

    public RelayCommand RefreshPortsCommand { get; }

    public RelayCommand AddToolCommand { get; }

    public AsyncRelayCommand ConnectCommand { get; }

    public AsyncRelayCommand DisconnectCommand { get; }

    public AsyncRelayCommand ZeroXCommand { get; }

    public AsyncRelayCommand ZeroYCommand { get; }

    public AsyncRelayCommand ZeroZCommand { get; }

    public AsyncRelayCommand ZeroAllCommand { get; }

    public AsyncRelayCommand HomeCommand { get; }

    public AsyncRelayCommand SoftResetCommand { get; }

    public AsyncRelayCommand UnlockCommand { get; }

    public RelayCommand SetSpindleMaxSpeedCommand { get; }

    public AsyncRelayCommand ApplySpindleSpeedCommand { get; }

    public AsyncRelayCommand StopSpindleCommand { get; }

    public AsyncRelayCommand SetWorkXCommand { get; }

    public AsyncRelayCommand SetWorkYCommand { get; }

    public AsyncRelayCommand SetWorkZCommand { get; }

    public AsyncRelayCommand SetXFromDiameterCommand { get; }

    public AsyncRelayCommand GoToXCommand { get; }

    public AsyncRelayCommand GoToYCommand { get; }

    public AsyncRelayCommand GoToZCommand { get; }

    public AsyncRelayCommand GoToRadiusPlusOneCommand { get; }

    public AsyncRelayCommand JogXPositiveCommand { get; }

    public AsyncRelayCommand JogXNegativeCommand { get; }

    public AsyncRelayCommand JogYPositiveCommand { get; }

    public AsyncRelayCommand JogYNegativeCommand { get; }

    public AsyncRelayCommand JogZPositiveCommand { get; }

    public AsyncRelayCommand JogZNegativeCommand { get; }

    public AsyncRelayCommand JogAPositiveCommand { get; }

    public AsyncRelayCommand JogANegativeCommand { get; }

    public AsyncRelayCommand JogBPositiveCommand { get; }

    public AsyncRelayCommand JogBNegativeCommand { get; }

    public RelayCommand CaptureToolChangePositionCommand { get; }

    public RelayCommand CaptureProbeStartZCommand { get; }

    public AsyncRelayCommand GoToToolChangeCommand { get; }

    public AsyncRelayCommand GoSafeZCommand { get; }

    public AsyncRelayCommand GoToXYZeroCommand { get; }

    public AsyncRelayCommand CalibrateToolProbePlateCommand { get; }

    public AsyncRelayCommand RunToolProbeCommand { get; }

    public RelayCommand LoadProgramCommand { get; }

    public AsyncRelayCommand StartProgramCommand { get; }

    public AsyncRelayCommand PauseResumeProgramCommand { get; }

    public AsyncRelayCommand StopProgramCommand { get; }

    public string SelectedPort
    {
        get => _selectedPort;
        set
        {
            if (SetProperty(ref _selectedPort, value))
            {
                RefreshCommandStates();
            }
        }
    }

    public string BaudRateInput
    {
        get => _baudRateInput;
        set
        {
            if (SetProperty(ref _baudRateInput, value))
            {
                RefreshCommandStates();
            }
        }
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetProperty(ref _connectionStatus, value);
    }

    public string ControllerState
    {
        get => _controllerState;
        private set => SetProperty(ref _controllerState, value);
    }

    public MachineMode MachineMode => _machineMode;

    public bool IsLatheMode => _machineMode == MachineMode.Lathe;

    public bool IsMillMode => _machineMode == MachineMode.Mill;

    public string MachineModeDisplayName => IsLatheMode ? "Lathe" : "Mill";

    public double MachineX
    {
        get => _machineX;
        private set => SetProperty(ref _machineX, value);
    }

    public double MachineY
    {
        get => _machineY;
        private set => SetProperty(ref _machineY, value);
    }

    public double MachineZ
    {
        get => _machineZ;
        private set => SetProperty(ref _machineZ, value);
    }

    public double MachineA
    {
        get => _machineA;
        private set => SetProperty(ref _machineA, value);
    }

    public double MachineB
    {
        get => _machineB;
        private set => SetProperty(ref _machineB, value);
    }

    public double WorkX
    {
        get => _workX;
        private set
        {
            if (SetProperty(ref _workX, value))
            {
                OnPropertyChanged(nameof(CurrentWorkOffsetText));
                OnPropertyChanged(nameof(PreviewHorizontalPosition));
            }
        }
    }

    public double WorkY
    {
        get => _workY;
        private set
        {
            if (SetProperty(ref _workY, value))
            {
                OnPropertyChanged(nameof(CurrentWorkOffsetText));
                OnPropertyChanged(nameof(PreviewVerticalPosition));
            }
        }
    }

    public double WorkZ
    {
        get => _workZ;
        private set
        {
            if (SetProperty(ref _workZ, value))
            {
                OnPropertyChanged(nameof(CurrentWorkOffsetText));
                OnPropertyChanged(nameof(PreviewHorizontalPosition));
            }
        }
    }

    public double WorkA
    {
        get => _workA;
        private set => SetProperty(ref _workA, value);
    }

    public double WorkB
    {
        get => _workB;
        private set => SetProperty(ref _workB, value);
    }

    public int ActiveToolNumber
    {
        get => _activeToolNumber;
        private set
        {
            if (SetProperty(ref _activeToolNumber, value))
            {
                OnPropertyChanged(nameof(ActiveToolText));
                OnPropertyChanged(nameof(MasterRelativeOffsetText));
            }
        }
    }

    public string ActiveToolText => $"Active tool T{ActiveToolNumber}";

    public string MasterRelativeOffsetText
    {
        get
        {
            if (!TryGetStoredToolOffsets(ActiveToolNumber, out var xOffset, out var zOffset))
            {
                return $"T{ActiveToolNumber} tool-tip offset unavailable";
            }

            return $"T{ActiveToolNumber} tip vs T0: X {xOffset:0.###} mm | Z {zOffset:0.###} mm";
        }
    }

    public string CurrentWorkOffsetText => IsLatheMode
        ? $"Displayed work X {WorkX:0.###} mm | Z {WorkZ:0.###} mm"
        : $"Displayed work X {WorkX:0.###} mm | Y {WorkY:0.###} mm | Z {WorkZ:0.###} mm";

    public string MillToolProbeStatusText
    {
        get
        {
            if (!IsMillMode)
            {
                return string.Empty;
            }

            if (!_millProbeReferenceWorkZ.HasValue)
            {
                return "Plate reference not calibrated for this setup.";
            }

            if (!_millLastProbeWorkZ.HasValue ||
                Math.Abs(_millLastProbeWorkZ.Value - _millProbeReferenceWorkZ.Value) < 0.0005d)
            {
                return $"Plate reference touch saved at work Z {_millProbeReferenceWorkZ.Value:0.###} mm.";
            }

            return
                $"Plate reference work Z {_millProbeReferenceWorkZ.Value:0.###} mm | last probe work Z {_millLastProbeWorkZ.Value:0.###} mm.";
        }
    }

    public double PreviewHorizontalPosition => IsLatheMode ? WorkZ : WorkX;

    public double PreviewVerticalPosition => IsLatheMode ? WorkX : WorkY;

    public string PreviewHorizontalAxisLabel => IsLatheMode ? "Z" : "X";

    public string PreviewVerticalAxisLabel => IsLatheMode ? "X" : "Y";

    public bool IsKeyboardControlEnabled
    {
        get => _isKeyboardControlEnabled;
        set
        {
            if (SetProperty(ref _isKeyboardControlEnabled, value))
            {
                OnPropertyChanged(nameof(KeyboardControlStatusText));
                OnPropertyChanged(nameof(IsKeyboardXAxisActive));
                OnPropertyChanged(nameof(IsKeyboardYAxisActive));
                OnPropertyChanged(nameof(IsKeyboardZAxisActive));
                OnPropertyChanged(nameof(IsKeyboardAAxisActive));
                OnPropertyChanged(nameof(IsKeyboardBAxisActive));
            }
        }
    }

    public string KeyboardControlStatusText => IsKeyboardControlEnabled ? "Keyboard control on" : "Keyboard control off";

    public string KeyboardAxisText => $"Axis {KeyboardJogAxis}";

    public bool IsKeyboardXAxisActive => IsKeyboardControlEnabled && KeyboardJogAxis == 'X';

    public bool IsKeyboardYAxisActive => IsKeyboardControlEnabled && KeyboardJogAxis == 'Y';

    public bool IsKeyboardZAxisActive => IsKeyboardControlEnabled && KeyboardJogAxis == 'Z';

    public bool IsKeyboardAAxisActive => IsKeyboardControlEnabled && KeyboardJogAxis == 'A';

    public bool IsKeyboardBAxisActive => IsKeyboardControlEnabled && KeyboardJogAxis == 'B';

    public string KeyboardStepText => $"Step {GetActiveKeyboardStep():0.###} mm";

    public string KeyboardFeedText => $"Feed {GetActiveKeyboardFeedText()}";

    public bool IsToolOffsetsLocked
    {
        get => _isToolOffsetsLocked;
        set => SetProperty(ref _isToolOffsetsLocked, value);
    }

    public bool XLimitPinHigh
    {
        get => _xLimitPinHigh;
        private set => SetProperty(ref _xLimitPinHigh, value);
    }

    public bool YLimitPinHigh
    {
        get => _yLimitPinHigh;
        private set => SetProperty(ref _yLimitPinHigh, value);
    }

    public bool ZLimitPinHigh
    {
        get => _zLimitPinHigh;
        private set => SetProperty(ref _zLimitPinHigh, value);
    }

    public bool ProbePinHigh
    {
        get => _probePinHigh;
        private set => SetProperty(ref _probePinHigh, value);
    }

    public bool CanManualSpindleControl => IsConnected && !IsProgramRunning;

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(CanManualSpindleControl));
                RefreshCommandStates();
            }
        }
    }

    public bool IsProgramRunning
    {
        get => _isProgramRunning;
        private set
        {
            if (SetProperty(ref _isProgramRunning, value))
            {
                OnPropertyChanged(nameof(PauseResumeLabel));
                OnPropertyChanged(nameof(ProgramSummaryText));
                OnPropertyChanged(nameof(CanManualSpindleControl));
                RefreshCommandStates();
            }
        }
    }

    public bool IsProgramPaused
    {
        get => _isProgramPaused;
        private set
        {
            if (SetProperty(ref _isProgramPaused, value))
            {
                OnPropertyChanged(nameof(PauseResumeLabel));
                OnPropertyChanged(nameof(ProgramSummaryText));
                RefreshCommandStates();
            }
        }
    }

    public string PauseResumeLabel => IsProgramPaused ? "Resume" : "Pause";

    public string WorkXInput
    {
        get => _workXInput;
        set => SetProperty(ref _workXInput, value);
    }

    public string WorkYInput
    {
        get => _workYInput;
        set => SetProperty(ref _workYInput, value);
    }

    public string WorkZInput
    {
        get => _workZInput;
        set => SetProperty(ref _workZInput, value);
    }

    public string DiameterTouchOffInput
    {
        get => _diameterTouchOffInput;
        set => SetProperty(ref _diameterTouchOffInput, value);
    }

    public string GoToXInput
    {
        get => _goToXInput;
        set => SetProperty(ref _goToXInput, value);
    }

    public string GoToYInput
    {
        get => _goToYInput;
        set => SetProperty(ref _goToYInput, value);
    }

    public string GoToZInput
    {
        get => _goToZInput;
        set => SetProperty(ref _goToZInput, value);
    }

    public string NewToolNumberInput
    {
        get => _newToolNumberInput;
        set
        {
            if (SetProperty(ref _newToolNumberInput, value))
            {
                AddToolCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string XJogFeedInput
    {
        get => _xJogFeedInput;
        set
        {
            if (SetProperty(ref _xJogFeedInput, value))
            {
                OnPropertyChanged(nameof(KeyboardFeedText));
                PersistMachineSettings();
            }
        }
    }

    public string YJogFeedInput
    {
        get => _yJogFeedInput;
        set
        {
            if (SetProperty(ref _yJogFeedInput, value))
            {
                OnPropertyChanged(nameof(KeyboardFeedText));
                PersistMachineSettings();
            }
        }
    }

    public string ZJogFeedInput
    {
        get => _zJogFeedInput;
        set
        {
            if (SetProperty(ref _zJogFeedInput, value))
            {
                OnPropertyChanged(nameof(KeyboardFeedText));
                PersistMachineSettings();
            }
        }
    }

    public string AJogFeedInput
    {
        get => _aJogFeedInput;
        set
        {
            if (SetProperty(ref _aJogFeedInput, value))
            {
                OnPropertyChanged(nameof(KeyboardFeedText));
                PersistMachineSettings();
            }
        }
    }

    public string BJogFeedInput
    {
        get => _bJogFeedInput;
        set
        {
            if (SetProperty(ref _bJogFeedInput, value))
            {
                OnPropertyChanged(nameof(KeyboardFeedText));
                PersistMachineSettings();
            }
        }
    }

    public string ToolChangeXInput
    {
        get => _toolChangeXInput;
        set
        {
            if (SetProperty(ref _toolChangeXInput, value))
            {
                PersistMachineSettings();
            }
        }
    }

    public string ToolChangeYInput
    {
        get => _toolChangeYInput;
        set
        {
            if (SetProperty(ref _toolChangeYInput, value))
            {
                PersistMachineSettings();
            }
        }
    }

    public string ToolChangeSafeZInput
    {
        get => _toolChangeSafeZInput;
        set
        {
            if (SetProperty(ref _toolChangeSafeZInput, value))
            {
                PersistMachineSettings();
            }
        }
    }

    public string ProbeStartZInput
    {
        get => _probeStartZInput;
        set
        {
            if (SetProperty(ref _probeStartZInput, value))
            {
                PersistMachineSettings();
            }
        }
    }

    public string ProbeTravelInput
    {
        get => _probeTravelInput;
        set
        {
            if (SetProperty(ref _probeTravelInput, value))
            {
                PersistMachineSettings();
            }
        }
    }

    public string ProbeFeedInput
    {
        get => _probeFeedInput;
        set
        {
            if (SetProperty(ref _probeFeedInput, value))
            {
                PersistMachineSettings();
            }
        }
    }

    public string ProbeFineFeedInput
    {
        get => _probeFineFeedInput;
        set
        {
            if (SetProperty(ref _probeFineFeedInput, value))
            {
                PersistMachineSettings();
            }
        }
    }

    public string ProbeRetractInput
    {
        get => _probeRetractInput;
        set
        {
            if (SetProperty(ref _probeRetractInput, value))
            {
                PersistMachineSettings();
            }
        }
    }

    public string SpindleMaxSpeedInput
    {
        get => _spindleMaxSpeedInput;
        set
        {
            if (SetProperty(ref _spindleMaxSpeedInput, value))
            {
                SetSpindleMaxSpeedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int SpindleMaxSpeed
    {
        get => _spindleMaxSpeed;
        private set
        {
            var normalizedValue = Math.Max(1, value);
            if (SetProperty(ref _spindleMaxSpeed, normalizedValue))
            {
                if (SelectedSpindleSpeed > normalizedValue)
                {
                    SelectedSpindleSpeed = normalizedValue;
                }

                OnPropertyChanged(nameof(SelectedSpindleSpeedText));
                PersistMachineSettings();
            }
        }
    }

    public int SelectedSpindleSpeed
    {
        get => _selectedSpindleSpeed;
        set
        {
            var clampedValue = Math.Clamp(value, 0, SpindleMaxSpeed);
            if (SetProperty(ref _selectedSpindleSpeed, clampedValue))
            {
                OnPropertyChanged(nameof(SelectedSpindleSpeedText));
            }
        }
    }

    public string SelectedSpindleSpeedText => $"S{SelectedSpindleSpeed} / max S{SpindleMaxSpeed}";

    public string SelectedWorkCoordinateSystem
    {
        get => _selectedWorkCoordinateSystem;
        set
        {
            var normalizedValue = NormalizeWorkCoordinateSystem(value);
            if (SetProperty(ref _selectedWorkCoordinateSystem, normalizedValue))
            {
                _lastAppliedWorkCoordinateSystem = string.Empty;
                PersistMachineSettings();
            }
        }
    }

    public int FeedOverridePercent
    {
        get => _feedOverridePercent;
        set
        {
            var clampedValue = Math.Clamp(value, 25, 200);
            if (SetProperty(ref _feedOverridePercent, clampedValue))
            {
                OnPropertyChanged(nameof(FeedOverrideText));

                if (!_isUpdatingFeedOverrideFromController)
                {
                    ScheduleFeedOverrideUpdate();
                }
            }
        }
    }

    public string FeedOverrideText => $"{FeedOverridePercent}%";

    public double SelectedXJogStep
    {
        get => _selectedXJogStep;
        set
        {
            if (SetProperty(ref _selectedXJogStep, value))
            {
                OnPropertyChanged(nameof(KeyboardStepText));
                PersistMachineSettings();
            }
        }
    }

    public double SelectedYJogStep
    {
        get => _selectedYJogStep;
        set
        {
            if (SetProperty(ref _selectedYJogStep, value))
            {
                OnPropertyChanged(nameof(KeyboardStepText));
                PersistMachineSettings();
            }
        }
    }

    public double SelectedZJogStep
    {
        get => _selectedZJogStep;
        set
        {
            if (SetProperty(ref _selectedZJogStep, value))
            {
                OnPropertyChanged(nameof(KeyboardStepText));
                PersistMachineSettings();
            }
        }
    }

    public double SelectedAJogStep
    {
        get => _selectedAJogStep;
        set
        {
            if (SetProperty(ref _selectedAJogStep, value))
            {
                OnPropertyChanged(nameof(KeyboardStepText));
                PersistMachineSettings();
            }
        }
    }

    public double SelectedBJogStep
    {
        get => _selectedBJogStep;
        set
        {
            if (SetProperty(ref _selectedBJogStep, value))
            {
                OnPropertyChanged(nameof(KeyboardStepText));
                PersistMachineSettings();
            }
        }
    }

    private char KeyboardJogAxis
    {
        get => _keyboardJogAxis;
        set
        {
            if (SetProperty(ref _keyboardJogAxis, value))
            {
                OnPropertyChanged(nameof(KeyboardAxisText));
                OnPropertyChanged(nameof(IsKeyboardXAxisActive));
                OnPropertyChanged(nameof(IsKeyboardYAxisActive));
                OnPropertyChanged(nameof(IsKeyboardZAxisActive));
                OnPropertyChanged(nameof(IsKeyboardAAxisActive));
                OnPropertyChanged(nameof(IsKeyboardBAxisActive));
                OnPropertyChanged(nameof(KeyboardStepText));
                OnPropertyChanged(nameof(KeyboardFeedText));
            }
        }
    }

    public string ProgramPath
    {
        get => _programPath;
        private set => SetProperty(ref _programPath, value);
    }

    public int ExecutedProgramLines
    {
        get => _executedProgramLines;
        private set
        {
            if (SetProperty(ref _executedProgramLines, value))
            {
                OnPropertyChanged(nameof(ProgramProgressText));
            }
        }
    }

    public double ProgramProgressPercent
    {
        get => _programProgressPercent;
        private set => SetProperty(ref _programProgressPercent, value);
    }

    public IReadOnlyList<ToolPathSegment> ToolPathSegments
    {
        get => _toolPathSegments;
        private set => SetProperty(ref _toolPathSegments, value);
    }

    public string ProgramSummaryText
    {
        get
        {
            if (_loadedProgram is null)
            {
                return "No G-code file loaded.";
            }

            var runtimeState = IsProgramRunning
                ? IsProgramPaused ? "Paused" : "Running"
                : "Ready";

            return $"{_loadedProgram.DisplayName} | {_loadedProgram.ExecutableLineCount} executable lines | {runtimeState}";
        }
    }

    public string ProgramProgressText
    {
        get
        {
            if (_loadedProgram is null)
            {
                return "Load a .nc, .tap, .gcode, or .txt file.";
            }

            return $"{ExecutedProgramLines} / {_loadedProgram.ExecutableLineCount} lines sent";
        }
    }

    public void Dispose()
    {
        foreach (var toolOffsetEntry in ToolOffsets)
        {
            UnregisterToolOffsetEntry(toolOffsetEntry);
        }

        _feedOverrideAdjustmentCancellation?.Cancel();
        _feedOverrideAdjustmentCancellation?.Dispose();
        _grblClient.StatusReceived -= OnGrblStatusReceived;
        _grblClient.MessageReceived -= OnGrblMessageReceived;
        _programCancellation?.Cancel();
        _grblClient.Dispose();
    }

    private void RefreshPorts()
    {
        var ports = SerialPort.GetPortNames()
            .OrderBy(portName => portName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        AvailablePorts.Clear();
        foreach (var port in ports)
        {
            AvailablePorts.Add(port);
        }

        if (string.IsNullOrWhiteSpace(SelectedPort) || !ports.Contains(SelectedPort, StringComparer.OrdinalIgnoreCase))
        {
            SelectedPort = ports.FirstOrDefault() ?? string.Empty;
        }

        AddLog(ports.Length == 0 ? "No serial ports detected." : $"Detected {ports.Length} serial port(s).");
        RefreshCommandStates();
    }

    private bool CanConnect()
    {
        return !IsConnected &&
               !string.IsNullOrWhiteSpace(SelectedPort) &&
               int.TryParse(BaudRateInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out var baudRate) &&
               baudRate > 0;
    }

    private bool CanOffsetOrJog()
    {
        return IsConnected && !IsProgramRunning;
    }

    private async Task CalibrateToolProbePlateAsync()
    {
        if (!IsMillMode)
        {
            return;
        }

        if (!IsConnected)
        {
            ShowValidationError("Connect to the controller before calibrating the plate reference.");
            return;
        }

        if (!TryGetMillProbeSettings(
                out var toolChangeX,
                out var toolChangeY,
                out var safeZ,
                out var probeStartZ,
                out var probeTravel,
                out var probeFeed,
                out var probeFineFeed,
                out var probeRetract))
        {
            return;
        }

        try
        {
            AddLog("Starting mill plate-reference calibration probe.");
            var probeResult = await ExecuteMillProbeCycleAsync(
                toolChangeX,
                toolChangeY,
                safeZ,
                probeStartZ,
                probeTravel,
                probeFeed,
                probeFineFeed,
                probeRetract);

            _millProbeReferenceWorkZ = probeResult.WorkZ;
            _millLastProbeWorkZ = probeResult.WorkZ;
            OnPropertyChanged(nameof(MillToolProbeStatusText));
            AddLog($"Calibrated mill plate reference from probe touch at work Z {probeResult.WorkZ:0.###} mm (machine Z {probeResult.MachineZ:0.###} mm).");
            await ReturnMillProbeToSafeZAsync(safeZ);
        }
        catch (Exception exception)
        {
            ShowOperationError("Plate reference calibration failed", exception);
        }
    }

    private bool CanStartProgram()
    {
        return IsConnected && _loadedProgram is not null && !IsProgramRunning;
    }

    private bool CanPauseProgram()
    {
        return IsConnected && IsProgramRunning;
    }

    private bool CanStopProgram()
    {
        return IsConnected && IsProgramRunning;
    }

    private async Task ConnectAsync()
    {
        if (!int.TryParse(BaudRateInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out var baudRate) || baudRate <= 0)
        {
            ShowValidationError("Enter a valid baud rate.");
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedPort))
        {
            ShowValidationError("Select a COM port before connecting.");
            return;
        }

        try
        {
            ConnectionStatus = $"Connecting to {SelectedPort}...";
            ControllerState = "Waiting for status";
            await _grblClient.ConnectAsync(SelectedPort, baudRate);
            IsConnected = true;
            _lastAppliedWorkCoordinateSystem = string.Empty;
            UpdateFeedOverrideFromController(100);
            XLimitPinHigh = false;
            YLimitPinHigh = false;
            ZLimitPinHigh = false;
            ProbePinHigh = false;
            ConnectionStatus = $"Connected to {SelectedPort}";
            AddLog($"Connected to {SelectedPort}.");
        }
        catch (Exception exception)
        {
            ConnectionStatus = "Disconnected";
            ControllerState = "Offline";
            ShowOperationError("Connection failed", exception);
        }
    }

    private async Task DisconnectAsync()
    {
        try
        {
            _programCancellation?.Cancel();
            await _grblClient.DisconnectAsync();
        }
        catch (Exception exception)
        {
            ShowOperationError("Disconnect failed", exception);
        }
        finally
        {
            _feedOverrideAdjustmentCancellation?.Cancel();
            IsConnected = false;
            _lastAppliedWorkCoordinateSystem = string.Empty;
            IsProgramRunning = false;
            IsProgramPaused = false;
            XLimitPinHigh = false;
            YLimitPinHigh = false;
            ZLimitPinHigh = false;
            ProbePinHigh = false;
            UpdateFeedOverrideFromController(100);
            ConnectionStatus = "Disconnected";
            ControllerState = "Offline";
        }
    }

    private async Task SetWorkXAsync()
    {
        if (!TryParseDouble(WorkXInput, "work X", out var xValue))
        {
            return;
        }

        await SetWorkCoordinateAsync(xValue: xValue);
    }

    private async Task SetWorkYAsync()
    {
        if (!TryParseDouble(WorkYInput, "work Y", out var yValue))
        {
            return;
        }

        await SetWorkCoordinateAsync(yValue: yValue);
    }

    private async Task SetWorkZAsync()
    {
        if (!TryParseDouble(WorkZInput, "work Z", out var zValue))
        {
            return;
        }

        await SetWorkCoordinateAsync(zValue: zValue);
    }

    private async Task HomeAsync()
    {
        try
        {
            AddLog("Starting homing cycle.");
            await _grblClient.HomeAsync();
        }
        catch (Exception exception)
        {
            ShowOperationError("Home failed", exception);
        }
    }

    private async Task SetXFromDiameterAsync()
    {
        if (!TryParseDouble(DiameterTouchOffInput, "touch-off diameter", out var diameter) || diameter < 0)
        {
            ShowValidationError("Enter a non-negative diameter for X touch-off.");
            return;
        }

        var xRadius = diameter / 2d;
        WorkXInput = xRadius.ToString("0.###", CultureInfo.InvariantCulture);
        await SetWorkCoordinateAsync(xValue: xRadius);
    }

    private async Task GoToXAsync()
    {
        if (!TryParseDouble(GoToXInput, "go-to X", out var targetX))
        {
            return;
        }

        await MoveToWorkCoordinateAsync(
            xValue: targetX,
            feedRateInput: XJogFeedInput,
            feedRateLabel: "X go-to feed",
            successMessage: $"Moving X to {targetX:0.###} mm.");
    }

    private async Task GoToZAsync()
    {
        if (!TryParseDouble(GoToZInput, "go-to Z", out var targetZ))
        {
            return;
        }

        await MoveToWorkCoordinateAsync(
            zValue: targetZ,
            feedRateInput: ZJogFeedInput,
            feedRateLabel: "Z go-to feed",
            successMessage: $"Moving Z to {targetZ:0.###} mm.");
    }

    private async Task GoToYAsync()
    {
        if (!TryParseDouble(GoToYInput, "go-to Y", out var targetY))
        {
            return;
        }

        await MoveToWorkCoordinateAsync(
            yValue: targetY,
            feedRateInput: YJogFeedInput,
            feedRateLabel: "Y go-to feed",
            successMessage: $"Moving Y to {targetY:0.###} mm.");
    }

    private async Task GoToRadiusPlusOneAsync()
    {
        if (!TryParseDouble(DiameterTouchOffInput, "touch-off diameter", out var diameter) || diameter < 0)
        {
            ShowValidationError("Enter a non-negative diameter to use Radius +1.");
            return;
        }

        var targetX = (diameter / 2d) + 1d;
        await MoveToWorkCoordinateAsync(
            xValue: targetX,
            feedRateInput: XJogFeedInput,
            feedRateLabel: "X go-to feed",
            successMessage: $"Moving X to radius +1 at {targetX:0.###} mm.");
    }

    private async Task MoveToWorkCoordinateAsync(
        double? xValue = null,
        double? yValue = null,
        double? zValue = null,
        double? aValue = null,
        double? bValue = null,
        string? feedRateInput = null,
        string feedRateLabel = "go-to feed",
        string successMessage = "Move command sent.")
    {
        if (!TryParseDouble(feedRateInput ?? string.Empty, feedRateLabel, out var feedRate) || feedRate <= 0)
        {
            ShowValidationError($"Enter a positive {feedRateLabel}.");
            return;
        }

        try
        {
            await EnsureSelectedWorkCoordinateSystemActiveAsync();
            await _grblClient.MoveToAsync(xValue, yValue, zValue, aValue, bValue, feedRate);
            AddLog(successMessage);
        }
        catch (Exception exception)
        {
            ShowOperationError("Go-to move failed", exception);
        }
    }

    private async Task SetWorkCoordinateAsync(
        double? xValue = null,
        double? yValue = null,
        double? zValue = null,
        double? aValue = null,
        double? bValue = null)
    {
        try
        {
            await _grblClient.SetWorkCoordinateOffsetAsync(
                xValue,
                yValue,
                zValue,
                aValue,
                bValue,
                workCoordinateSystem: SelectedWorkCoordinateSystem);
            await EnsureSelectedWorkCoordinateSystemActiveAsync();
            AddLog(BuildOffsetLogMessage(xValue, yValue, zValue, aValue, bValue, SelectedWorkCoordinateSystem));

            if (IsMillMode && zValue.HasValue)
            {
                ClearMillToolProbeCalibration("Mill plate reference cleared because work Z changed. Recalibrate with the reference tool.");
            }
        }
        catch (Exception exception)
        {
            ShowOperationError("Work offset update failed", exception);
        }
    }

    private async Task JogAxisAsync(string axisLetter, double distanceMillimeters, string feedRateInput)
    {
        if (!TryParseDouble(feedRateInput, $"{axisLetter} jog feed", out var feedRate) || feedRate <= 0)
        {
            ShowValidationError($"Enter a positive jog feed for {axisLetter}.");
            return;
        }

        try
        {
            await _grblClient.JogAsync(axisLetter, distanceMillimeters, feedRate);
        }
        catch (Exception exception)
        {
            ShowOperationError($"Jog {axisLetter} failed", exception);
        }
    }

    private async Task ApplySpindleSpeedAsync()
    {
        try
        {
            await _grblClient.SetSpindleSpeedAsync(SelectedSpindleSpeed);
            AddLog(SelectedSpindleSpeed == 0
                ? "Spindle stopped."
                : $"Spindle command sent with S{SelectedSpindleSpeed}.");
            PersistMachineSettings();
        }
        catch (Exception exception)
        {
            ShowOperationError("Spindle update failed", exception);
        }
    }

    private async Task StopSpindleAsync()
    {
        try
        {
            await _grblClient.StopSpindleAsync();
            AddLog("Spindle stop command sent.");
        }
        catch (Exception exception)
        {
            ShowOperationError("Spindle stop failed", exception);
        }
    }

    private async Task RunToolProbeAsync()
    {
        if (!IsMillMode)
        {
            return;
        }

        if (!_millProbeReferenceWorkZ.HasValue)
        {
            ShowValidationError("Calibrate the plate reference with the master tool before probing another tool.");
            return;
        }

        if (!TryGetMillProbeSettings(
                out var toolChangeX,
                out var toolChangeY,
                out var safeZ,
                out var probeStartZ,
                out var probeTravel,
                out var probeFeed,
                out var probeFineFeed,
                out var probeRetract))
        {
            return;
        }

        try
        {
            AddLog("Starting mill tool probe cycle.");
            var workZBeforeProbe = WorkZ;
            var probeResult = await ExecuteMillProbeCycleAsync(
                toolChangeX,
                toolChangeY,
                safeZ,
                probeStartZ,
                probeTravel,
                probeFeed,
                probeFineFeed,
                probeRetract);
            var workZAdjustment = _millProbeReferenceWorkZ.Value - probeResult.WorkZ;
            await _grblClient.SetWorkCoordinateOffsetAsync(
                z: _millProbeReferenceWorkZ.Value,
                workCoordinateSystem: SelectedWorkCoordinateSystem);
            await EnsureSelectedWorkCoordinateSystemActiveAsync();
            await ReturnMillProbeToSafeZAsync(safeZ);

            var desiredWorkZ = workZBeforeProbe + workZAdjustment;
            WorkZ = desiredWorkZ;
            WorkZInput = desiredWorkZ.ToString("0.###", CultureInfo.InvariantCulture);
            _millLastProbeWorkZ = probeResult.WorkZ;
            OnPropertyChanged(nameof(MillToolProbeStatusText));
            AddLog(
                $"Tool probe complete. Tool touched at work Z {probeResult.WorkZ:0.###} mm (machine Z {probeResult.MachineZ:0.###} mm), so work Z was adjusted by {workZAdjustment:+0.###;-0.###;0} mm. Current work Z is {desiredWorkZ:0.###} mm.");
        }
        catch (Exception exception)
        {
            ShowOperationError("Tool probe failed", exception);
        }
    }

    public bool TryHandleKeyboardInput(Key key, ModifierKeys modifiers, bool isRepeat)
    {
        if (!IsKeyboardControlEnabled || modifiers != ModifierKeys.None)
        {
            return false;
        }

        switch (key)
        {
            case Key.Left:
                ExecuteKeyboardJog(positiveDirection: false);
                return true;
            case Key.Right:
                ExecuteKeyboardJog(positiveDirection: true);
                return true;
            case Key.Up:
                if (!isRepeat)
                {
                    AdjustKeyboardStep(1);
                }

                return true;
            case Key.Down:
                if (!isRepeat)
                {
                    AdjustKeyboardStep(-1);
                }

                return true;
            case Key.A:
                if (!isRepeat)
                {
                    KeyboardJogAxis = GetNextKeyboardAxis();
                }

                return true;
            case Key.OemComma:
                if (!isRepeat)
                {
                    AdjustKeyboardFeed(-10);
                }

                return true;
            case Key.OemPeriod:
                if (!isRepeat)
                {
                    AdjustKeyboardFeed(10);
                }

                return true;
            default:
                return false;
        }
    }

    private bool CanAddTool()
    {
        return TryParseToolNumber(NewToolNumberInput, out _);
    }

    private bool CanSetSpindleMaxSpeed()
    {
        return TryParsePositiveInt(SpindleMaxSpeedInput, out _);
    }

    private void SetSpindleMaxSpeed()
    {
        if (!TryParsePositiveInt(SpindleMaxSpeedInput, out var spindleMaxSpeed))
        {
            ShowValidationError("Enter a positive whole number for spindle max speed.");
            return;
        }

        SpindleMaxSpeed = spindleMaxSpeed;
        SpindleMaxSpeedInput = spindleMaxSpeed.ToString(CultureInfo.InvariantCulture);
        AddLog($"Spindle max speed set to S{SpindleMaxSpeed}.");
    }

    private void LoadPersistedMachineSettings()
    {
        try
        {
            var storedSettings = MachineSettingsStorage.Load();
            if (storedSettings is null)
            {
                return;
            }

            _suppressMachineSettingsPersistence = true;

            SpindleMaxSpeed = storedSettings.SpindleMaxSpeed > 0 ? storedSettings.SpindleMaxSpeed : 1000;
            SpindleMaxSpeedInput = SpindleMaxSpeed.ToString(CultureInfo.InvariantCulture);
            SelectedSpindleSpeed = storedSettings.SelectedSpindleSpeed;
            SelectedWorkCoordinateSystem = string.IsNullOrWhiteSpace(storedSettings.SelectedWorkCoordinateSystem)
                ? "G54"
                : storedSettings.SelectedWorkCoordinateSystem;
            XJogFeedInput = storedSettings.XJogFeedInput;
            YJogFeedInput = storedSettings.YJogFeedInput;
            ZJogFeedInput = storedSettings.ZJogFeedInput;
            AJogFeedInput = storedSettings.AJogFeedInput;
            BJogFeedInput = storedSettings.BJogFeedInput;
            SelectedXJogStep = storedSettings.SelectedXJogStep > 0 ? storedSettings.SelectedXJogStep : XJogSteps[2];
            SelectedYJogStep = storedSettings.SelectedYJogStep > 0 ? storedSettings.SelectedYJogStep : LinearJogSteps[2];
            SelectedZJogStep = storedSettings.SelectedZJogStep > 0 ? storedSettings.SelectedZJogStep : LinearJogSteps[4];
            SelectedAJogStep = storedSettings.SelectedAJogStep > 0 ? storedSettings.SelectedAJogStep : RotaryJogSteps[2];
            SelectedBJogStep = storedSettings.SelectedBJogStep > 0 ? storedSettings.SelectedBJogStep : RotaryJogSteps[2];
            ToolChangeXInput = storedSettings.ToolChangeXInput;
            ToolChangeYInput = storedSettings.ToolChangeYInput;
            ToolChangeSafeZInput = storedSettings.ToolChangeSafeZInput;
            ProbeStartZInput = string.IsNullOrWhiteSpace(storedSettings.ProbeStartZInput)
                ? storedSettings.ToolChangeSafeZInput
                : storedSettings.ProbeStartZInput;
            ProbeTravelInput = storedSettings.ProbeTravelInput;
            ProbeFeedInput = storedSettings.ProbeFeedInput;
            ProbeFineFeedInput = string.IsNullOrWhiteSpace(storedSettings.ProbeFineFeedInput)
                ? storedSettings.ProbeFeedInput
                : storedSettings.ProbeFineFeedInput;
            ProbeRetractInput = storedSettings.ProbeRetractInput;
            AddLog("Loaded persisted machine settings.");
        }
        catch (Exception exception)
        {
            AddLog($"Machine settings load failed: {exception.Message}");
        }
        finally
        {
            _suppressMachineSettingsPersistence = false;
        }
    }

    private void PersistMachineSettings()
    {
        if (_suppressMachineSettingsPersistence)
        {
            return;
        }

        try
        {
            MachineSettingsStorage.Save(new MachineSettingsStorageEntry(
                SelectedSpindleSpeed,
                SpindleMaxSpeed,
                SelectedWorkCoordinateSystem,
                XJogFeedInput,
                YJogFeedInput,
                ZJogFeedInput,
                AJogFeedInput,
                BJogFeedInput,
                SelectedXJogStep,
                SelectedYJogStep,
                SelectedZJogStep,
                SelectedAJogStep,
                SelectedBJogStep,
                ToolChangeXInput,
                ToolChangeYInput,
                ToolChangeSafeZInput,
                ProbeStartZInput,
                ProbeTravelInput,
                ProbeFeedInput,
                ProbeFineFeedInput,
                ProbeRetractInput));
        }
        catch (Exception exception)
        {
            AddLog($"Machine settings save failed: {exception.Message}");
        }
    }

    private void LoadPersistedToolOffsets()
    {
        try
        {
            var storedEntries = ToolOffsetStorage.Load();
            if (storedEntries.Count == 0)
            {
                return;
            }

            _suppressToolOffsetPersistence = true;

            foreach (var storedEntry in storedEntries)
            {
                var (entry, _) = AddOrGetToolOffsetEntry(storedEntry.ToolNumber);
                entry.XOffsetInput = storedEntry.XOffsetInput;
                entry.ZOffsetInput = storedEntry.ZOffsetInput;
            }

            AddLog($"Loaded {storedEntries.Count} persisted tool offset entr{(storedEntries.Count == 1 ? "y" : "ies")}.");
        }
        catch (Exception exception)
        {
            AddLog($"Tool offset load failed: {exception.Message}");
        }
        finally
        {
            _suppressToolOffsetPersistence = false;
        }
    }

    private void EnsureMasterToolEntry()
    {
        var (masterEntry, created) = AddOrGetToolOffsetEntry(0);
        if (masterEntry.XOffsetInput != "0")
        {
            masterEntry.XOffsetInput = "0";
        }

        if (masterEntry.ZOffsetInput != "0")
        {
            masterEntry.ZOffsetInput = "0";
        }

        if (created)
        {
            AddLog("Added master tool T0.");
        }
    }

    private void PersistToolOffsets()
    {
        if (_suppressToolOffsetPersistence)
        {
            return;
        }

        try
        {
            ToolOffsetStorage.Save(ToolOffsets.Select(entry =>
                new ToolOffsetStorageEntry(entry.ToolNumber, entry.XOffsetInput, entry.ZOffsetInput)));
        }
        catch (Exception exception)
        {
            AddLog($"Tool offset save failed: {exception.Message}");
        }
    }

    private void RegisterToolOffsetEntry(ToolOffsetEntryViewModel toolOffsetEntry)
    {
        toolOffsetEntry.PropertyChanged += OnToolOffsetEntryPropertyChanged;
    }

    private void UnregisterToolOffsetEntry(ToolOffsetEntryViewModel toolOffsetEntry)
    {
        toolOffsetEntry.PropertyChanged -= OnToolOffsetEntryPropertyChanged;
    }

    private void OnToolOffsetEntryPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(ToolOffsetEntryViewModel.XOffsetInput) or nameof(ToolOffsetEntryViewModel.ZOffsetInput))
        {
            PersistToolOffsets();
            OnPropertyChanged(nameof(MasterRelativeOffsetText));
        }
    }

    private void CaptureToolChangePosition()
    {
        if (!IsConnected)
        {
            ShowValidationError("Connect to the controller before capturing the tool change X/Y position.");
            return;
        }

        ToolChangeXInput = MachineX.ToString("0.###", CultureInfo.InvariantCulture);
        ToolChangeYInput = MachineY.ToString("0.###", CultureInfo.InvariantCulture);
        AddLog($"Captured tool change position from current machine X/Y: X {MachineX:0.###}, Y {MachineY:0.###}.");
    }

    private void CaptureProbeStartZ()
    {
        if (!IsConnected)
        {
            ShowValidationError("Connect to the controller before capturing the probe start Z position.");
            return;
        }

        ProbeStartZInput = MachineZ.ToString("0.###", CultureInfo.InvariantCulture);
        AddLog($"Captured probe start Z from current machine Z: Z {MachineZ:0.###}.");
    }

    private async Task GoToToolChangeAsync()
    {
        if (!TryGetMillToolChangeSettings(out var toolChangeX, out var toolChangeY, out var safeZ))
        {
            return;
        }

        try
        {
            AddLog($"Moving to safe Z {safeZ:0.###} mm and tool change X/Y {toolChangeX:0.###}, {toolChangeY:0.###}.");
            await MoveToMillToolChangeAsync(toolChangeX, toolChangeY, safeZ);
        }
        catch (Exception exception)
        {
            ShowOperationError("Go to tool change failed", exception);
        }
    }

    private async Task GoSafeZAsync()
    {
        if (!TryParseDouble(ToolChangeSafeZInput, "safe Z", out var safeZ))
        {
            return;
        }

        try
        {
            AddLog($"Moving to safe Z {safeZ:0.###} mm.");
            await _grblClient.MoveToMachineAsync(z: safeZ);
        }
        catch (Exception exception)
        {
            ShowOperationError("Go safe Z failed", exception);
        }
    }

    private async Task GoToXYZeroAsync()
    {
        if (!TryGetMillPlanarFeedRate(out var feedRate))
        {
            return;
        }

        try
        {
            await EnsureSelectedWorkCoordinateSystemActiveAsync();
            await _grblClient.MoveToAsync(x: 0, y: 0, feedRateMillimetersPerMinute: feedRate);
            AddLog($"Moving to work X 0 / Y 0 at F{feedRate:0.###}.");
        }
        catch (Exception exception)
        {
            ShowOperationError("Go to X/Y zero failed", exception);
        }
    }

    private void AddTool()
    {
        if (!TryParseToolNumber(NewToolNumberInput, out var toolNumber))
        {
            ShowValidationError("Enter a non-negative tool number.");
            return;
        }

        var (entry, created) = AddOrGetToolOffsetEntry(toolNumber);
        if (!created)
        {
            AddLog($"Tool T{toolNumber} already exists in the offset list.");
        }
        else
        {
            AddLog($"Added tool T{toolNumber} to the offset list.");
        }

        NewToolNumberInput = string.Empty;
    }

    private void LoadProgram()
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "G-code files|*.nc;*.tap;*.gcode;*.txt|All files|*.*",
            Title = "Load G-code program"
        };

        if (openFileDialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _loadedProgram = GCodeParser.ParseFile(openFileDialog.FileName, MachineMode);
            ToolPathSegments = _loadedProgram.Segments;
            ProgramPath = _loadedProgram.FilePath;
            ExecutedProgramLines = 0;
            ProgramProgressPercent = 0;
            OnPropertyChanged(nameof(ProgramSummaryText));
            OnPropertyChanged(nameof(ProgramProgressText));
            if (IsLatheMode)
            {
                MergeToolOffsetsFromProgram(_loadedProgram);
            }
            AddLog($"Loaded program: {_loadedProgram.DisplayName}");
            RefreshCommandStates();
        }
        catch (Exception exception)
        {
            ShowOperationError("Program load failed", exception);
        }
    }

    private void MergeToolOffsetsFromProgram(GCodeProgram program)
    {
        var addedTools = 0;
        foreach (var toolNumber in program.ToolNumbers)
        {
            var (_, created) = AddOrGetToolOffsetEntry(toolNumber);
            if (created)
            {
                addedTools++;
            }
        }

        if (addedTools > 0)
        {
            AddLog($"Added {addedTools} tool offset entr{(addedTools == 1 ? "y" : "ies")} from program.");
        }
    }

    private async Task StartProgramAsync()
    {
        if (_loadedProgram is null)
        {
            ShowValidationError("Load a G-code file before starting playback.");
            return;
        }

        _programCancellation = new CancellationTokenSource();
        IsProgramRunning = true;
        IsProgramPaused = false;
        ExecutedProgramLines = 0;
        ProgramProgressPercent = 0;
        await EnsureSelectedWorkCoordinateSystemActiveAsync();
        AddLog($"Starting program: {_loadedProgram.DisplayName}");

        try
        {
            var progress = new Progress<(int completedLines, int totalLines)>(update =>
            {
                ExecutedProgramLines = update.completedLines;
                ProgramProgressPercent = update.totalLines == 0
                    ? 0
                    : (double)update.completedLines / update.totalLines * 100d;
            });

            await StreamProgramBlocksAsync(_loadedProgram.Blocks, progress, _programCancellation.Token);
            AddLog("Program complete.");
        }
        catch (OperationCanceledException)
        {
            AddLog("Program playback stopped.");
        }
        catch (Exception exception)
        {
            ShowOperationError("Program playback failed", exception);
        }
        finally
        {
            _programCancellation?.Dispose();
            _programCancellation = null;
            IsProgramRunning = false;
            IsProgramPaused = false;
            _lastAppliedWorkCoordinateSystem = string.Empty;
            OnPropertyChanged(nameof(ProgramSummaryText));
        }
    }

    private async Task StreamProgramBlocksAsync(
        IReadOnlyList<GCodeBlock> blocks,
        IProgress<(int completedLines, int totalLines)>? progress,
        CancellationToken cancellationToken)
    {
        var promptedToolBlockIndexes = new HashSet<int>();
        var totalCommands = blocks.Count(block => block.ShouldSendToController);
        var completedCommands = 0;

        for (var index = 0; index < blocks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var block = blocks[index];

            if (!block.IsPauseCommand &&
                block.ToolNumber.HasValue &&
                !promptedToolBlockIndexes.Contains(index))
            {
                await PromptForToolOffsetsAsync(block.ToolNumber.Value, cancellationToken);
                promptedToolBlockIndexes.Add(index);
            }

            await WaitForProgramResumeAsync(cancellationToken);

            if (block.ShouldSendToController)
            {
                await _grblClient.SendCommandAsync(block.CommandLine, cancellationToken);
                completedCommands++;
                progress?.Report((completedCommands, totalCommands));
            }

            if (!block.IsPauseCommand)
            {
                continue;
            }

            if (!IsProgramPaused)
            {
                IsProgramPaused = true;
                AddLog("Program paused by G-code.");
            }

            if (block.ToolNumber.HasValue && !promptedToolBlockIndexes.Contains(index))
            {
                await PromptForToolOffsetsAsync(block.ToolNumber.Value, cancellationToken);
                promptedToolBlockIndexes.Add(index);
            }
            else if (index + 1 < blocks.Count &&
                     blocks[index + 1].ToolNumber.HasValue &&
                     !promptedToolBlockIndexes.Contains(index + 1))
            {
                await PromptForToolOffsetsAsync(blocks[index + 1].ToolNumber!.Value, cancellationToken);
                promptedToolBlockIndexes.Add(index + 1);
            }

            await WaitForProgramResumeAsync(cancellationToken);
        }
    }

    private async Task PauseResumeProgramAsync()
    {
        try
        {
            if (IsProgramPaused)
            {
                await _grblClient.ResumeAsync();
                IsProgramPaused = false;
                AddLog("Program resumed.");
            }
            else
            {
                await _grblClient.FeedHoldAsync();
                IsProgramPaused = true;
                AddLog("Program paused.");
            }
        }
        catch (Exception exception)
        {
            ShowOperationError("Pause/resume failed", exception);
        }
    }

    private async Task StopProgramAsync()
    {
        await SendSoftResetAsync("Program playback stopped. Soft reset sent to GRBL.", "Stop failed");
    }

    private async Task SoftResetAsync()
    {
        await SendSoftResetAsync("Soft reset sent to GRBL.", "Soft reset failed");
    }

    private async Task UnlockAsync()
    {
        try
        {
            await _grblClient.UnlockAsync();
            AddLog("GRBL unlock ($X) sent.");
        }
        catch (Exception exception)
        {
            ShowOperationError("Unlock failed", exception);
        }
    }

    private async Task SendSoftResetAsync(string successMessage, string errorCaption)
    {
        try
        {
            _programCancellation?.Cancel();
            _feedOverrideAdjustmentCancellation?.Cancel();
            await _grblClient.SoftResetAsync();
            IsProgramRunning = false;
            IsProgramPaused = false;
            _lastAppliedWorkCoordinateSystem = string.Empty;
            UpdateFeedOverrideFromController(100);
            ControllerState = "Resetting";
            AddLog(successMessage);
        }
        catch (Exception exception)
        {
            ShowOperationError(errorCaption, exception);
        }
    }

    private async Task WaitForProgramResumeAsync(CancellationToken cancellationToken)
    {
        while (IsProgramPaused)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(75, cancellationToken);
        }
    }

    private async Task PromptForToolOffsetsAsync(int toolNumber, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsLatheMode)
        {
            return;
        }

        var toolEntry = ToolOffsets.FirstOrDefault(entry => entry.ToolNumber == toolNumber);
        if (toolEntry is null)
        {
            var missingToolResult = MessageBox.Show(
                $"Tool T{toolNumber} is referenced in the program, but no stored offsets were found. Continue without applying offsets?",
                "Tool Offsets",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (missingToolResult != MessageBoxResult.Yes)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            AddLog($"Continuing without stored offsets for T{toolNumber}.");
            return;
        }

        var result = MessageBox.Show(
            $"Tool T{toolNumber} was requested by the program. Apply its stored X/Z offsets now?",
            "Tool Offsets",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Cancel)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (result == MessageBoxResult.Yes)
        {
            await ApplyToolOffsetAsync(toolEntry, $"Applied stored offsets for T{toolNumber}.");
        }
        else
        {
            AddLog($"Skipped stored offsets for T{toolNumber}.");
        }
    }

    private (ToolOffsetEntryViewModel entry, bool created) AddOrGetToolOffsetEntry(int toolNumber)
    {
        var existingEntry = ToolOffsets.FirstOrDefault(entry => entry.ToolNumber == toolNumber);
        if (existingEntry is not null)
        {
            return (existingEntry, false);
        }

        var newEntry = new ToolOffsetEntryViewModel(toolNumber, CaptureToolOffsetFromCurrentPosition, ApplyToolOffsetAsync, DeleteToolOffsetEntry);
        var insertIndex = 0;
        while (insertIndex < ToolOffsets.Count && ToolOffsets[insertIndex].ToolNumber < toolNumber)
        {
            insertIndex++;
        }

        RegisterToolOffsetEntry(newEntry);
        ToolOffsets.Insert(insertIndex, newEntry);
        PersistToolOffsets();
        return (newEntry, true);
    }

    private void CaptureToolOffsetFromCurrentPosition(ToolOffsetEntryViewModel toolEntry)
    {
        if (!IsConnected)
        {
            ShowValidationError("Connect to the controller before capturing a tool offset.");
            return;
        }

        if (toolEntry.ToolNumber == 0)
        {
            toolEntry.CaptureOffsets(0, 0);
            AddLog("T0 remains the master tool at X 0 / Z 0.");
            return;
        }

        toolEntry.CaptureOffsets(-WorkX, -WorkZ);
        AddLog($"Captured T{toolEntry.ToolNumber} tool-tip offset from T0.");
    }

    private void DeleteToolOffsetEntry(ToolOffsetEntryViewModel toolEntry)
    {
        if (toolEntry.ToolNumber == 0)
        {
            ShowValidationError("T0 is the master tool and cannot be removed.");
            return;
        }

        if (toolEntry.ToolNumber == ActiveToolNumber)
        {
            ShowValidationError($"Use T0 or another tool before deleting active tool T{toolEntry.ToolNumber}.");
            return;
        }

        UnregisterToolOffsetEntry(toolEntry);
        ToolOffsets.Remove(toolEntry);
        PersistToolOffsets();
        AddLog($"Deleted tool T{toolEntry.ToolNumber} from the offset list.");
    }

    private void ExecuteKeyboardJog(bool positiveDirection)
    {
        var command = KeyboardJogAxis switch
        {
            'X' => positiveDirection ? JogXPositiveCommand : JogXNegativeCommand,
            'Y' => positiveDirection ? JogYPositiveCommand : JogYNegativeCommand,
            'Z' => positiveDirection ? JogZPositiveCommand : JogZNegativeCommand,
            'A' => positiveDirection ? JogAPositiveCommand : JogANegativeCommand,
            'B' => positiveDirection ? JogBPositiveCommand : JogBNegativeCommand,
            _ => positiveDirection ? JogXPositiveCommand : JogXNegativeCommand
        };

        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private void AdjustKeyboardStep(int direction)
    {
        switch (KeyboardJogAxis)
        {
            case 'X':
                SelectedXJogStep = GetAdjacentStep(XJogSteps, SelectedXJogStep, direction);
                break;
            case 'Y':
                SelectedYJogStep = GetAdjacentStep(LinearJogSteps, SelectedYJogStep, direction);
                break;
            case 'Z':
                SelectedZJogStep = GetAdjacentStep(ZJogSteps, SelectedZJogStep, direction);
                break;
            case 'A':
                SelectedAJogStep = GetAdjacentStep(RotaryJogSteps, SelectedAJogStep, direction);
                break;
            case 'B':
                SelectedBJogStep = GetAdjacentStep(RotaryJogSteps, SelectedBJogStep, direction);
                break;
        }
    }

    private void AdjustKeyboardFeed(double delta)
    {
        switch (KeyboardJogAxis)
        {
            case 'X':
                XJogFeedInput = AdjustFeedInput(XJogFeedInput, delta);
                break;
            case 'Y':
                YJogFeedInput = AdjustFeedInput(YJogFeedInput, delta);
                break;
            case 'Z':
                ZJogFeedInput = AdjustFeedInput(ZJogFeedInput, delta);
                break;
            case 'A':
                AJogFeedInput = AdjustFeedInput(AJogFeedInput, delta);
                break;
            case 'B':
                BJogFeedInput = AdjustFeedInput(BJogFeedInput, delta);
                break;
        }
    }

    private double GetActiveKeyboardStep()
    {
        return KeyboardJogAxis switch
        {
            'X' => SelectedXJogStep,
            'Y' => SelectedYJogStep,
            'Z' => SelectedZJogStep,
            'A' => SelectedAJogStep,
            'B' => SelectedBJogStep,
            _ => SelectedXJogStep
        };
    }

    private string GetActiveKeyboardFeedText()
    {
        return KeyboardJogAxis switch
        {
            'X' => XJogFeedInput,
            'Y' => YJogFeedInput,
            'Z' => ZJogFeedInput,
            'A' => AJogFeedInput,
            'B' => BJogFeedInput,
            _ => XJogFeedInput
        };
    }

    private char GetNextKeyboardAxis()
    {
        var supportedAxes = IsMillMode
            ? new[] { 'X', 'Y', 'Z', 'A', 'B' }
            : new[] { 'X', 'Z' };
        var currentIndex = Array.IndexOf(supportedAxes, KeyboardJogAxis);
        if (currentIndex < 0)
        {
            return supportedAxes[0];
        }

        return supportedAxes[(currentIndex + 1) % supportedAxes.Length];
    }

    private static double GetAdjacentStep(IReadOnlyList<double> steps, double currentStep, int direction)
    {
        if (steps.Count == 0)
        {
            return currentStep;
        }

        var nearestIndex = 0;
        var nearestDistance = double.MaxValue;
        for (var index = 0; index < steps.Count; index++)
        {
            var distance = Math.Abs(steps[index] - currentStep);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = index;
            }
        }

        var targetIndex = Math.Clamp(nearestIndex + direction, 0, steps.Count - 1);
        return steps[targetIndex];
    }

    private static string AdjustFeedInput(string currentInput, double delta)
    {
        if (!TryParseFlexibleDouble(currentInput, out var currentFeed) || currentFeed <= 0)
        {
            currentFeed = 10;
        }

        return Math.Max(1, currentFeed + delta).ToString("0.###", CultureInfo.InvariantCulture);
    }

    private Task ApplyToolOffsetAsync(ToolOffsetEntryViewModel toolEntry)
    {
        return ApplyToolOffsetAsync(toolEntry, $"Applied offsets for T{toolEntry.ToolNumber}.");
    }

    private async Task ApplyToolOffsetAsync(ToolOffsetEntryViewModel toolEntry, string successMessage)
    {
        if (!IsConnected)
        {
            ShowValidationError("Connect to the controller before applying a tool offset.");
            return;
        }

        if (!TryGetStoredToolOffsets(toolEntry.ToolNumber, out var targetToolX, out var targetToolZ))
        {
            ShowValidationError($"Enter valid X and Z offsets for T{toolEntry.ToolNumber}.");
            return;
        }

        if (!TryGetStoredToolOffsets(ActiveToolNumber, out var currentToolX, out var currentToolZ))
        {
            ShowValidationError($"Enter valid X and Z offsets for the active tool T{ActiveToolNumber} before switching tools.");
            return;
        }

        try
        {
            var desiredWorkX = WorkX + (targetToolX - currentToolX);
            var desiredWorkZ = WorkZ + (targetToolZ - currentToolZ);
            await _grblClient.SetWorkCoordinateOffsetAsync(
                x: desiredWorkX,
                z: desiredWorkZ,
                workCoordinateSystem: SelectedWorkCoordinateSystem);
            await EnsureSelectedWorkCoordinateSystemActiveAsync();
            WorkX = desiredWorkX;
            WorkZ = desiredWorkZ;
            ActiveToolNumber = toolEntry.ToolNumber;
            AddLog(successMessage);
        }
        catch (Exception exception)
        {
            ShowOperationError("Tool offset apply failed", exception);
        }
    }

    private bool TryGetStoredToolOffsets(int toolNumber, out double xOffset, out double zOffset)
    {
        if (toolNumber == 0)
        {
            xOffset = 0;
            zOffset = 0;
            return true;
        }

        var toolEntry = ToolOffsets.FirstOrDefault(entry => entry.ToolNumber == toolNumber);
        if (toolEntry is not null && toolEntry.TryGetOffsets(out xOffset, out zOffset))
        {
            return true;
        }

        xOffset = 0;
        zOffset = 0;
        return false;
    }

    private void OnGrblStatusReceived(object? sender, GrblStatus status)
    {
        _ = _dispatcher.BeginInvoke(() =>
        {
            if (status.MachineX.HasValue)
            {
                MachineX = status.MachineX.Value;
            }

            if (status.MachineY.HasValue)
            {
                MachineY = status.MachineY.Value;
            }

            if (status.MachineZ.HasValue)
            {
                MachineZ = status.MachineZ.Value;
            }

            if (status.MachineA.HasValue)
            {
                MachineA = status.MachineA.Value;
            }

            if (status.MachineB.HasValue)
            {
                MachineB = status.MachineB.Value;
            }

            if (status.WorkX.HasValue)
            {
                WorkX = status.WorkX.Value;
            }

            if (status.WorkY.HasValue)
            {
                WorkY = status.WorkY.Value;
            }

            if (status.WorkZ.HasValue)
            {
                WorkZ = status.WorkZ.Value;
            }

            if (status.WorkA.HasValue)
            {
                WorkA = status.WorkA.Value;
            }

            if (status.WorkB.HasValue)
            {
                WorkB = status.WorkB.Value;
            }

            XLimitPinHigh = status.XLimitPinHigh;
            YLimitPinHigh = status.YLimitPinHigh;
            ZLimitPinHigh = status.ZLimitPinHigh;
            ProbePinHigh = status.ProbePinHigh;

            if (status.FeedOverridePercent.HasValue)
            {
                UpdateFeedOverrideFromController(status.FeedOverridePercent.Value);
            }

            if (IsProgramRunning)
            {
                var controllerPaused = IsControllerPausedState(status.State);
                if (IsProgramPaused != controllerPaused)
                {
                    IsProgramPaused = controllerPaused;
                }
            }

            ControllerState = status.State;
        }, DispatcherPriority.Background);
    }

    private void OnGrblMessageReceived(object? sender, string message)
    {
        _ = _dispatcher.BeginInvoke(() => AddLog(message), DispatcherPriority.Background);
    }

    private void AddLog(string message)
    {
        var timestampedMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";
        ControllerLog.Insert(0, timestampedMessage);

        while (ControllerLog.Count > 200)
        {
            ControllerLog.RemoveAt(ControllerLog.Count - 1);
        }
    }

    private void ShowValidationError(string message)
    {
        AddLog(message);
        MessageBox.Show(message, "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ShowOperationError(string caption, Exception exception)
    {
        var message = $"{caption}: {exception.Message}";
        AddLog(message);
        MessageBox.Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void RefreshCommandStates()
    {
        RefreshPortsCommand.RaiseCanExecuteChanged();
        AddToolCommand.RaiseCanExecuteChanged();
        ConnectCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        ZeroXCommand.RaiseCanExecuteChanged();
        ZeroYCommand.RaiseCanExecuteChanged();
        ZeroZCommand.RaiseCanExecuteChanged();
        ZeroAllCommand.RaiseCanExecuteChanged();
        HomeCommand.RaiseCanExecuteChanged();
        SoftResetCommand.RaiseCanExecuteChanged();
        UnlockCommand.RaiseCanExecuteChanged();
        SetSpindleMaxSpeedCommand.RaiseCanExecuteChanged();
        ApplySpindleSpeedCommand.RaiseCanExecuteChanged();
        StopSpindleCommand.RaiseCanExecuteChanged();
        SetWorkXCommand.RaiseCanExecuteChanged();
        SetWorkYCommand.RaiseCanExecuteChanged();
        SetWorkZCommand.RaiseCanExecuteChanged();
        SetXFromDiameterCommand.RaiseCanExecuteChanged();
        GoToXCommand.RaiseCanExecuteChanged();
        GoToYCommand.RaiseCanExecuteChanged();
        GoToZCommand.RaiseCanExecuteChanged();
        GoToRadiusPlusOneCommand.RaiseCanExecuteChanged();
        JogXPositiveCommand.RaiseCanExecuteChanged();
        JogXNegativeCommand.RaiseCanExecuteChanged();
        JogYPositiveCommand.RaiseCanExecuteChanged();
        JogYNegativeCommand.RaiseCanExecuteChanged();
        JogZPositiveCommand.RaiseCanExecuteChanged();
        JogZNegativeCommand.RaiseCanExecuteChanged();
        JogAPositiveCommand.RaiseCanExecuteChanged();
        JogANegativeCommand.RaiseCanExecuteChanged();
        JogBPositiveCommand.RaiseCanExecuteChanged();
        JogBNegativeCommand.RaiseCanExecuteChanged();
        CaptureToolChangePositionCommand.RaiseCanExecuteChanged();
        CaptureProbeStartZCommand.RaiseCanExecuteChanged();
        GoToToolChangeCommand.RaiseCanExecuteChanged();
        GoSafeZCommand.RaiseCanExecuteChanged();
        GoToXYZeroCommand.RaiseCanExecuteChanged();
        CalibrateToolProbePlateCommand.RaiseCanExecuteChanged();
        RunToolProbeCommand.RaiseCanExecuteChanged();
        LoadProgramCommand.RaiseCanExecuteChanged();
        StartProgramCommand.RaiseCanExecuteChanged();
        PauseResumeProgramCommand.RaiseCanExecuteChanged();
        StopProgramCommand.RaiseCanExecuteChanged();
    }

    private bool CanAdjustManualSpindle()
    {
        return CanManualSpindleControl;
    }

    private void ScheduleFeedOverrideUpdate()
    {
        if (!IsConnected)
        {
            return;
        }

        _feedOverrideAdjustmentCancellation?.Cancel();
        _feedOverrideAdjustmentCancellation?.Dispose();

        _feedOverrideAdjustmentCancellation = new CancellationTokenSource();
        var cancellationToken = _feedOverrideAdjustmentCancellation.Token;
        var targetPercent = FeedOverridePercent;

        _ = ApplyFeedOverrideAsync(targetPercent, cancellationToken);
    }

    private async Task ApplyFeedOverrideAsync(int targetPercent, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(120, cancellationToken);
            await _grblClient.SetFeedOverrideAsync(targetPercent, _lastKnownFeedOverridePercent, cancellationToken);
            _lastKnownFeedOverridePercent = targetPercent;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ShowOperationError("Feed override update failed", exception);
        }
    }

    private void UpdateFeedOverrideFromController(int overridePercent)
    {
        _lastKnownFeedOverridePercent = Math.Clamp(overridePercent, 25, 200);
        _isUpdatingFeedOverrideFromController = true;

        try
        {
            FeedOverridePercent = _lastKnownFeedOverridePercent;
        }
        finally
        {
            _isUpdatingFeedOverrideFromController = false;
        }
    }

    private static bool IsControllerPausedState(string controllerState)
    {
        return controllerState.StartsWith("Hold", StringComparison.OrdinalIgnoreCase) ||
               controllerState.StartsWith("Door", StringComparison.OrdinalIgnoreCase);
    }

    private void ClearMillToolProbeCalibration(string reason)
    {
        if (!IsMillMode || (!_millProbeReferenceWorkZ.HasValue && !_millLastProbeWorkZ.HasValue))
        {
            return;
        }

        _millProbeReferenceWorkZ = null;
        _millLastProbeWorkZ = null;
        OnPropertyChanged(nameof(MillToolProbeStatusText));
        AddLog(reason);
    }

    private async Task MoveToMillToolChangeAsync(double toolChangeX, double toolChangeY, double safeZ)
    {
        await _grblClient.MoveToMachineAsync(z: safeZ);
        await _grblClient.MoveToMachineAsync(x: toolChangeX, y: toolChangeY);
    }

    private async Task WaitForMillProbeStatusAsync(double startingMachineZ, double startingWorkZ)
    {
        const int maxAttempts = 20;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            await Task.Delay(50);

            if ((ProbePinHigh || Math.Abs(MachineZ - startingMachineZ) > 0.0005d) &&
                Math.Abs(WorkZ - startingWorkZ) > 0.0005d)
            {
                return;
            }
        }

        AddLog("Probe finished before a fresh status report arrived; using the latest reported position.");
    }

    private async Task WaitForProbePinStateAsync(bool expectedHigh, string timeoutMessage)
    {
        const int maxAttempts = 20;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (ProbePinHigh == expectedHigh)
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new InvalidOperationException(timeoutMessage);
    }

    private async Task<bool> WaitForMachineZAsync(double expectedMachineZ)
    {
        const int maxAttempts = 40;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (Math.Abs(MachineZ - expectedMachineZ) <= 0.05d)
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }

    private async Task ReturnMillProbeToSafeZAsync(double safeZ)
    {
        await _grblClient.MoveToMachineAsync(z: safeZ);
        if (!await WaitForMachineZAsync(safeZ))
        {
            AddLog("Probe cycle completed, but status did not confirm the return-to-safe-Z move in time.");
        }
    }

    private async Task<(double MachineZ, double WorkZ)> ExecuteMillProbeCycleAsync(
        double toolChangeX,
        double toolChangeY,
        double safeZ,
        double probeStartZ,
        double probeTravel,
        double probeFeed,
        double probeFineFeed,
        double probeRetract)
    {
        await MoveToMillToolChangeAsync(toolChangeX, toolChangeY, safeZ);
        await _grblClient.MoveToMachineAsync(z: probeStartZ);

        var startingMachineZ = MachineZ;
        var startingWorkZ = WorkZ;
        await _grblClient.ProbeAxisRelativeAsync("Z", -Math.Abs(probeTravel), probeFeed);
        await WaitForMillProbeStatusAsync(startingMachineZ, startingWorkZ);

        var coarseProbeMachineZ = MachineZ;
        await _grblClient.MoveToMachineAsync(z: coarseProbeMachineZ + Math.Abs(probeRetract));
        await WaitForProbePinStateAsync(expectedHigh: false, "Probe input stayed active after the first pull-off. Increase pull-off or check the probe plate wiring.");

        var fineProbeStartMachineZ = MachineZ;
        var fineProbeStartWorkZ = WorkZ;
        var fineProbeTravel = Math.Max(Math.Abs(probeRetract) * 2d, 1d);
        await _grblClient.ProbeAxisRelativeAsync("Z", -fineProbeTravel, probeFineFeed);
        await WaitForMillProbeStatusAsync(fineProbeStartMachineZ, fineProbeStartWorkZ);

        var confirmedMachineZ = MachineZ;
        var confirmedWorkZ = WorkZ;
        return (confirmedMachineZ, confirmedWorkZ);
    }

    private async Task EnsureSelectedWorkCoordinateSystemActiveAsync()
    {
        var normalizedWcs = NormalizeWorkCoordinateSystem(SelectedWorkCoordinateSystem);
        if (string.Equals(_lastAppliedWorkCoordinateSystem, normalizedWcs, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await _grblClient.SelectWorkCoordinateSystemAsync(normalizedWcs);
        _lastAppliedWorkCoordinateSystem = normalizedWcs;
        AddLog($"Selected {normalizedWcs} as the active work coordinate system.");
    }

    private bool TryGetMillToolChangeSettings(out double toolChangeX, out double toolChangeY, out double safeZ)
    {
        toolChangeX = 0;
        toolChangeY = 0;
        safeZ = 0;

        if (!TryParseDouble(ToolChangeXInput, "tool change X", out toolChangeX) ||
            !TryParseDouble(ToolChangeYInput, "tool change Y", out toolChangeY) ||
            !TryParseDouble(ToolChangeSafeZInput, "safe Z", out safeZ))
        {
            return false;
        }

        return true;
    }

    private bool TryGetMillProbeSettings(
        out double toolChangeX,
        out double toolChangeY,
        out double safeZ,
        out double probeStartZ,
        out double probeTravel,
        out double probeFeed,
        out double probeFineFeed,
        out double probeRetract)
    {
        toolChangeX = 0;
        toolChangeY = 0;
        safeZ = 0;
        probeStartZ = 0;
        probeTravel = 0;
        probeFeed = 0;
        probeFineFeed = 0;
        probeRetract = 0;

        if (!TryGetMillToolChangeSettings(out toolChangeX, out toolChangeY, out safeZ) ||
            !TryParseDouble(ProbeStartZInput, "probe start Z", out probeStartZ) ||
            !TryParseDouble(ProbeTravelInput, "probe travel", out probeTravel) ||
            !TryParseDouble(ProbeFeedInput, "probe feed", out probeFeed) ||
            !TryParseDouble(ProbeFineFeedInput, "fine probe feed", out probeFineFeed) ||
            !TryParseDouble(ProbeRetractInput, "probe retract", out probeRetract))
        {
            return false;
        }

        if (probeTravel <= 0 || probeFeed <= 0 || probeFineFeed <= 0 || probeRetract <= 0)
        {
            ShowValidationError("Enter positive probe travel, coarse probe feed, fine probe feed, and pull-off values.");
            return false;
        }

        if (probeStartZ > safeZ)
        {
            ShowValidationError("Probe start Z should be at or below the safe Z height.");
            return false;
        }

        return true;
    }

    private bool TryGetMillPlanarFeedRate(out double feedRate)
    {
        if (!TryParseDouble(XJogFeedInput, "X jog feed", out var xFeedRate) ||
            !TryParseDouble(YJogFeedInput, "Y jog feed", out var yFeedRate))
        {
            feedRate = 0;
            return false;
        }

        if (xFeedRate <= 0 || yFeedRate <= 0)
        {
            ShowValidationError("Enter positive X and Y jog feeds before moving to work X/Y zero.");
            feedRate = 0;
            return false;
        }

        feedRate = Math.Min(xFeedRate, yFeedRate);
        return true;
    }

    private static string BuildOffsetLogMessage(double? xValue, double? yValue, double? zValue, double? aValue, double? bValue, string workCoordinateSystem)
    {
        var parts = new List<string>();
        if (xValue.HasValue)
        {
            parts.Add($"X={xValue.Value:0.###}");
        }

        if (yValue.HasValue)
        {
            parts.Add($"Y={yValue.Value:0.###}");
        }

        if (zValue.HasValue)
        {
            parts.Add($"Z={zValue.Value:0.###}");
        }

        if (aValue.HasValue)
        {
            parts.Add($"A={aValue.Value:0.###}");
        }

        if (bValue.HasValue)
        {
            parts.Add($"B={bValue.Value:0.###}");
        }

        var normalizedWcs = NormalizeWorkCoordinateSystem(workCoordinateSystem);
        return parts.Count == 0
            ? $"Updated {normalizedWcs} work offset."
            : $"Updated {normalizedWcs} work offset: {string.Join(", ", parts)}";
    }

    private bool TryParseDouble(string rawValue, string fieldName, out double parsedValue)
    {
        if (double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedValue))
        {
            return true;
        }

        if (double.TryParse(rawValue, NumberStyles.Float, CultureInfo.CurrentCulture, out parsedValue))
        {
            return true;
        }

        ShowValidationError($"Enter a valid number for {fieldName}.");
        return false;
    }

    private static bool TryParseToolNumber(string rawValue, out int toolNumber)
    {
        if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out toolNumber) && toolNumber >= 0)
        {
            return true;
        }

        if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.CurrentCulture, out toolNumber) && toolNumber >= 0)
        {
            return true;
        }

        toolNumber = 0;
        return false;
    }

    private static bool TryParsePositiveInt(string rawValue, out int parsedValue)
    {
        if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue) && parsedValue > 0)
        {
            return true;
        }

        return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsedValue) && parsedValue > 0;
    }

    private static bool TryParseFlexibleDouble(string rawValue, out double value)
    {
        if (double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return double.TryParse(rawValue, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    private static string NormalizeWorkCoordinateSystem(string? workCoordinateSystem)
    {
        var normalized = string.IsNullOrWhiteSpace(workCoordinateSystem)
            ? "G54"
            : workCoordinateSystem.Trim().ToUpperInvariant();

        return normalized is "G54" or "G55" or "G56" or "G57" or "G58" or "G59"
            ? normalized
            : "G54";
    }
}
