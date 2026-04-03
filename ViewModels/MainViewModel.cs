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

    private CancellationTokenSource? _programCancellation;
    private GCodeProgram? _loadedProgram;

    private string _selectedPort = string.Empty;
    private string _baudRateInput = "115200";
    private string _connectionStatus = "Disconnected";
    private string _controllerState = "Offline";
    private string _workXInput = "0";
    private string _workZInput = "0";
    private string _diameterTouchOffInput = "0";
    private string _goToXInput = "1";
    private string _goToZInput = "0";
    private string _newToolNumberInput = string.Empty;
    private string _xJogFeedInput = "200";
    private string _zJogFeedInput = "400";
    private string _programPath = "No file loaded";
    private double _machineX;
    private double _machineZ;
    private double _workX;
    private double _workZ;
    private bool _xLimitPinHigh;
    private bool _zLimitPinHigh;
    private bool _isConnected;
    private bool _isProgramRunning;
    private bool _isProgramPaused;
    private double _selectedXJogStep = 0.1;
    private double _selectedZJogStep = 1;
    private int _selectedSpindleSpeed = 500;
    private int _feedOverridePercent = 100;
    private int _lastKnownFeedOverridePercent = 100;
    private bool _isUpdatingFeedOverrideFromController;
    private int _executedProgramLines;
    private double _programProgressPercent;
    private IReadOnlyList<ToolPathSegment> _toolPathSegments = Array.Empty<ToolPathSegment>();
    private CancellationTokenSource? _feedOverrideAdjustmentCancellation;
    private bool _isToolOffsetsLocked = true;
    private bool _suppressToolOffsetPersistence;
    private int _activeToolNumber;

    public MainViewModel()
    {
        _dispatcher = Application.Current.Dispatcher;

        RefreshPortsCommand = new RelayCommand(RefreshPorts);
        AddToolCommand = new RelayCommand(AddTool, CanAddTool);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, CanConnect);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected);
        ZeroXCommand = new AsyncRelayCommand(() => SetWorkCoordinateAsync(xValue: 0), CanOffsetOrJog);
        ZeroZCommand = new AsyncRelayCommand(() => SetWorkCoordinateAsync(zValue: 0), CanOffsetOrJog);
        ZeroAllCommand = new AsyncRelayCommand(() => SetWorkCoordinateAsync(0, 0), CanOffsetOrJog);
        HomeCommand = new AsyncRelayCommand(HomeAsync, CanOffsetOrJog);
        SoftResetCommand = new AsyncRelayCommand(SoftResetAsync, () => IsConnected);
        ApplySpindleSpeedCommand = new AsyncRelayCommand(ApplySpindleSpeedAsync, CanAdjustManualSpindle);
        StopSpindleCommand = new AsyncRelayCommand(StopSpindleAsync, CanAdjustManualSpindle);
        SetWorkXCommand = new AsyncRelayCommand(SetWorkXAsync, CanOffsetOrJog);
        SetWorkZCommand = new AsyncRelayCommand(SetWorkZAsync, CanOffsetOrJog);
        SetXFromDiameterCommand = new AsyncRelayCommand(SetXFromDiameterAsync, CanOffsetOrJog);
        GoToXCommand = new AsyncRelayCommand(GoToXAsync, CanOffsetOrJog);
        GoToZCommand = new AsyncRelayCommand(GoToZAsync, CanOffsetOrJog);
        GoToRadiusPlusOneCommand = new AsyncRelayCommand(GoToRadiusPlusOneAsync, CanOffsetOrJog);
        JogXPositiveCommand = new AsyncRelayCommand(() => JogAxisAsync("X", SelectedXJogStep, XJogFeedInput), CanOffsetOrJog);
        JogXNegativeCommand = new AsyncRelayCommand(() => JogAxisAsync("X", -SelectedXJogStep, XJogFeedInput), CanOffsetOrJog);
        JogZPositiveCommand = new AsyncRelayCommand(() => JogAxisAsync("Z", SelectedZJogStep, ZJogFeedInput), CanOffsetOrJog);
        JogZNegativeCommand = new AsyncRelayCommand(() => JogAxisAsync("Z", -SelectedZJogStep, ZJogFeedInput), CanOffsetOrJog);
        LoadProgramCommand = new RelayCommand(LoadProgram, () => !IsProgramRunning);
        StartProgramCommand = new AsyncRelayCommand(StartProgramAsync, CanStartProgram);
        PauseResumeProgramCommand = new AsyncRelayCommand(PauseResumeProgramAsync, CanPauseProgram);
        StopProgramCommand = new AsyncRelayCommand(StopProgramAsync, CanStopProgram);

        _grblClient.StatusReceived += OnGrblStatusReceived;
        _grblClient.MessageReceived += OnGrblMessageReceived;

        LoadPersistedToolOffsets();
        EnsureMasterToolEntry();
        RefreshPorts();
    }

    public ObservableCollection<string> AvailablePorts { get; } = new();

    public ObservableCollection<string> ControllerLog { get; } = new();

    public ObservableCollection<ToolOffsetEntryViewModel> ToolOffsets { get; } = new();

    public IReadOnlyList<double> XJogSteps { get; } = [0.01, 0.05, 0.1, 0.5, 1, 5, 10];

    public IReadOnlyList<double> ZJogSteps { get; } = [0.01, 0.05, 0.1, 0.5, 1, 5, 10, 50, 100];

    public RelayCommand RefreshPortsCommand { get; }

    public RelayCommand AddToolCommand { get; }

    public AsyncRelayCommand ConnectCommand { get; }

    public AsyncRelayCommand DisconnectCommand { get; }

    public AsyncRelayCommand ZeroXCommand { get; }

    public AsyncRelayCommand ZeroZCommand { get; }

    public AsyncRelayCommand ZeroAllCommand { get; }

    public AsyncRelayCommand HomeCommand { get; }

    public AsyncRelayCommand SoftResetCommand { get; }

    public AsyncRelayCommand ApplySpindleSpeedCommand { get; }

    public AsyncRelayCommand StopSpindleCommand { get; }

    public AsyncRelayCommand SetWorkXCommand { get; }

    public AsyncRelayCommand SetWorkZCommand { get; }

    public AsyncRelayCommand SetXFromDiameterCommand { get; }

    public AsyncRelayCommand GoToXCommand { get; }

    public AsyncRelayCommand GoToZCommand { get; }

    public AsyncRelayCommand GoToRadiusPlusOneCommand { get; }

    public AsyncRelayCommand JogXPositiveCommand { get; }

    public AsyncRelayCommand JogXNegativeCommand { get; }

    public AsyncRelayCommand JogZPositiveCommand { get; }

    public AsyncRelayCommand JogZNegativeCommand { get; }

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

    public double MachineX
    {
        get => _machineX;
        private set => SetProperty(ref _machineX, value);
    }

    public double MachineZ
    {
        get => _machineZ;
        private set => SetProperty(ref _machineZ, value);
    }

    public double WorkX
    {
        get => _workX;
        private set
        {
            if (SetProperty(ref _workX, value))
            {
                OnPropertyChanged(nameof(CurrentWorkOffsetText));
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
            }
        }
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

    public string CurrentWorkOffsetText => $"Displayed work X {WorkX:0.###} mm | Z {WorkZ:0.###} mm";

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

    public bool ZLimitPinHigh
    {
        get => _zLimitPinHigh;
        private set => SetProperty(ref _zLimitPinHigh, value);
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
        set => SetProperty(ref _xJogFeedInput, value);
    }

    public string ZJogFeedInput
    {
        get => _zJogFeedInput;
        set => SetProperty(ref _zJogFeedInput, value);
    }

    public int SelectedSpindleSpeed
    {
        get => _selectedSpindleSpeed;
        set
        {
            var clampedValue = Math.Clamp(value, 0, 1000);
            if (SetProperty(ref _selectedSpindleSpeed, clampedValue))
            {
                OnPropertyChanged(nameof(SelectedSpindleSpeedText));
            }
        }
    }

    public string SelectedSpindleSpeedText => $"S{SelectedSpindleSpeed}";

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
        set => SetProperty(ref _selectedXJogStep, value);
    }

    public double SelectedZJogStep
    {
        get => _selectedZJogStep;
        set => SetProperty(ref _selectedZJogStep, value);
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
            UpdateFeedOverrideFromController(100);
            XLimitPinHigh = false;
            ZLimitPinHigh = false;
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
            IsProgramRunning = false;
            IsProgramPaused = false;
            XLimitPinHigh = false;
            ZLimitPinHigh = false;
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
        double? zValue = null,
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
            await _grblClient.MoveToAsync(xValue, zValue, feedRate);
            AddLog(successMessage);
        }
        catch (Exception exception)
        {
            ShowOperationError("Go-to move failed", exception);
        }
    }

    private async Task SetWorkCoordinateAsync(double? xValue = null, double? zValue = null)
    {
        try
        {
            await _grblClient.SetWorkCoordinateOffsetAsync(xValue, zValue);
            AddLog(BuildOffsetLogMessage(xValue, zValue));
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

    private bool CanAddTool()
    {
        return TryParseToolNumber(NewToolNumberInput, out _);
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
            _loadedProgram = GCodeParser.ParseFile(openFileDialog.FileName);
            ToolPathSegments = _loadedProgram.Segments;
            ProgramPath = _loadedProgram.FilePath;
            ExecutedProgramLines = 0;
            ProgramProgressPercent = 0;
            OnPropertyChanged(nameof(ProgramSummaryText));
            OnPropertyChanged(nameof(ProgramProgressText));
            MergeToolOffsetsFromProgram(_loadedProgram);
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

    private async Task SendSoftResetAsync(string successMessage, string errorCaption)
    {
        try
        {
            _programCancellation?.Cancel();
            _feedOverrideAdjustmentCancellation?.Cancel();
            await _grblClient.SoftResetAsync();
            IsProgramRunning = false;
            IsProgramPaused = false;
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
            await _grblClient.SetWorkCoordinateOffsetAsync(desiredWorkX, desiredWorkZ);
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

            if (status.MachineZ.HasValue)
            {
                MachineZ = status.MachineZ.Value;
            }

            if (status.WorkX.HasValue)
            {
                WorkX = status.WorkX.Value;
            }

            if (status.WorkZ.HasValue)
            {
                WorkZ = status.WorkZ.Value;
            }

            XLimitPinHigh = status.XLimitPinHigh;
            ZLimitPinHigh = status.ZLimitPinHigh;

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
        ZeroZCommand.RaiseCanExecuteChanged();
        ZeroAllCommand.RaiseCanExecuteChanged();
        HomeCommand.RaiseCanExecuteChanged();
        SoftResetCommand.RaiseCanExecuteChanged();
        ApplySpindleSpeedCommand.RaiseCanExecuteChanged();
        StopSpindleCommand.RaiseCanExecuteChanged();
        SetWorkXCommand.RaiseCanExecuteChanged();
        SetWorkZCommand.RaiseCanExecuteChanged();
        SetXFromDiameterCommand.RaiseCanExecuteChanged();
        GoToXCommand.RaiseCanExecuteChanged();
        GoToZCommand.RaiseCanExecuteChanged();
        GoToRadiusPlusOneCommand.RaiseCanExecuteChanged();
        JogXPositiveCommand.RaiseCanExecuteChanged();
        JogXNegativeCommand.RaiseCanExecuteChanged();
        JogZPositiveCommand.RaiseCanExecuteChanged();
        JogZNegativeCommand.RaiseCanExecuteChanged();
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

    private static string BuildOffsetLogMessage(double? xValue, double? zValue)
    {
        return (xValue, zValue) switch
        {
            ({ } x, { } z) => $"Updated work offset: X={x:0.###}, Z={z:0.###}",
            ({ } x, null) => $"Updated work X offset: X={x:0.###}",
            (null, { } z) => $"Updated work Z offset: Z={z:0.###}",
            _ => "Updated work offset."
        };
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
}
