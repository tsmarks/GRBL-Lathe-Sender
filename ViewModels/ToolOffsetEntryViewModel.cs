using System;
using System.Globalization;
using System.Threading.Tasks;
using GRBL_Lathe_Control.Infrastructure;

namespace GRBL_Lathe_Control.ViewModels;

public sealed class ToolOffsetEntryViewModel : ObservableObject
{
    private readonly Action<ToolOffsetEntryViewModel> _captureCurrentPosition;
    private readonly Func<ToolOffsetEntryViewModel, Task> _useOffsetAsync;
    private readonly Action<ToolOffsetEntryViewModel> _deleteOffset;
    private string _xOffsetInput = "0";
    private string _zOffsetInput = "0";

    public ToolOffsetEntryViewModel(
        int toolNumber,
        Action<ToolOffsetEntryViewModel> captureCurrentPosition,
        Func<ToolOffsetEntryViewModel, Task> useOffsetAsync,
        Action<ToolOffsetEntryViewModel> deleteOffset)
    {
        ToolNumber = toolNumber;
        _captureCurrentPosition = captureCurrentPosition;
        _useOffsetAsync = useOffsetAsync;
        _deleteOffset = deleteOffset;
        SetCommand = new RelayCommand(() => _captureCurrentPosition(this));
        UseCommand = new AsyncRelayCommand(() => _useOffsetAsync(this));
        DeleteCommand = new RelayCommand(() => _deleteOffset(this));
    }

    public int ToolNumber { get; }

    public bool CanDeleteTool => ToolNumber != 0;

    public string XOffsetInput
    {
        get => _xOffsetInput;
        set => SetProperty(ref _xOffsetInput, value);
    }

    public string ZOffsetInput
    {
        get => _zOffsetInput;
        set => SetProperty(ref _zOffsetInput, value);
    }

    public RelayCommand SetCommand { get; }

    public AsyncRelayCommand UseCommand { get; }

    public RelayCommand DeleteCommand { get; }

    public void CaptureOffsets(double workX, double workZ)
    {
        XOffsetInput = workX.ToString("0.###", CultureInfo.InvariantCulture);
        ZOffsetInput = workZ.ToString("0.###", CultureInfo.InvariantCulture);
    }

    public bool TryGetOffsets(out double xOffset, out double zOffset)
    {
        if (double.TryParse(XOffsetInput, NumberStyles.Float, CultureInfo.InvariantCulture, out xOffset) &&
            double.TryParse(ZOffsetInput, NumberStyles.Float, CultureInfo.InvariantCulture, out zOffset))
        {
            return true;
        }

        if (double.TryParse(XOffsetInput, NumberStyles.Float, CultureInfo.CurrentCulture, out xOffset) &&
            double.TryParse(ZOffsetInput, NumberStyles.Float, CultureInfo.CurrentCulture, out zOffset))
        {
            return true;
        }

        xOffset = 0;
        zOffset = 0;
        return false;
    }
}
