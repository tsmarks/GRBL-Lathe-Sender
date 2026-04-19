using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GRBL_Lathe_Control.Models;

namespace GRBL_Lathe_Control.Services;

public sealed class GrblClient : IDisposable
{
    private readonly object _portLock = new();
    private readonly object _receiveBufferLock = new();
    private readonly StringBuilder _receiveBuffer = new();
    private readonly ConcurrentQueue<TaskCompletionSource<string>> _pendingResponses = new();

    private CancellationTokenSource? _statusPollCancellation;
    private Task? _statusPollTask;
    private SerialPort? _serialPort;

    public event EventHandler<GrblStatus>? StatusReceived;

    public event EventHandler<string>? MessageReceived;

    public bool IsConnected => _serialPort?.IsOpen == true;

    public async Task ConnectAsync(string portName, int baudRate, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            throw new ArgumentException("A COM port must be selected.", nameof(portName));
        }

        await DisconnectAsync().ConfigureAwait(false);

        var serialPort = new SerialPort(portName, baudRate)
        {
            NewLine = "\n",
            DtrEnable = true,
            RtsEnable = true,
            Encoding = Encoding.ASCII,
            ReadTimeout = 250,
            WriteTimeout = 250
        };

        serialPort.DataReceived += OnSerialPortDataReceived;
        serialPort.ErrorReceived += OnSerialPortErrorReceived;

        try
        {
            serialPort.Open();
            _serialPort = serialPort;

            MessageReceived?.Invoke(this, $"Connected to {portName} at {baudRate} baud.");

            await Task.Delay(1500, cancellationToken).ConfigureAwait(false);

            _statusPollCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _statusPollTask = RunStatusPollingAsync(_statusPollCancellation.Token);
        }
        catch
        {
            serialPort.DataReceived -= OnSerialPortDataReceived;
            serialPort.ErrorReceived -= OnSerialPortErrorReceived;
            serialPort.Dispose();
            _serialPort = null;
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        var cancellation = Interlocked.Exchange(ref _statusPollCancellation, null);
        if (cancellation is not null)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

        if (_statusPollTask is not null)
        {
            try
            {
                await _statusPollTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _statusPollTask = null;
            }
        }

        var serialPort = Interlocked.Exchange(ref _serialPort, null);
        if (serialPort is not null)
        {
            serialPort.DataReceived -= OnSerialPortDataReceived;
            serialPort.ErrorReceived -= OnSerialPortErrorReceived;

            try
            {
                if (serialPort.IsOpen)
                {
                    serialPort.Close();
                }
            }
            catch
            {
            }
            finally
            {
                serialPort.Dispose();
            }
        }

        while (_pendingResponses.TryDequeue(out var pendingResponse))
        {
            pendingResponse.TrySetCanceled();
        }
        MessageReceived?.Invoke(this, "Disconnected.");
    }

    public Task JogAsync(string axisLetter, double distanceMillimeters, double feedRateMillimetersPerMinute, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(axisLetter))
        {
            throw new ArgumentException("An axis letter is required.", nameof(axisLetter));
        }

        var normalizedAxis = axisLetter.Trim().ToUpperInvariant();
        var command = string.Create(
            CultureInfo.InvariantCulture,
            $"$J=G91 G21 {normalizedAxis}{distanceMillimeters:0.###} F{feedRateMillimetersPerMinute:0.###}");

        return SendCommandAsync(command, cancellationToken);
    }

    public Task MoveToAsync(
        double? x = null,
        double? y = null,
        double? z = null,
        double? a = null,
        double? b = null,
        double? feedRateMillimetersPerMinute = null,
        CancellationToken cancellationToken = default)
    {
        if (!x.HasValue && !y.HasValue && !z.HasValue && !a.HasValue && !b.HasValue)
        {
            throw new ArgumentException("At least one axis target is required.");
        }

        if (!feedRateMillimetersPerMinute.HasValue || feedRateMillimetersPerMinute.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(feedRateMillimetersPerMinute), "A positive feed rate is required.");
        }

        var builder = new StringBuilder("G90 G21 G1");

        if (x.HasValue)
        {
            builder.Append(CultureInfo.InvariantCulture, $" X{x.Value:0.###}");
        }

        if (y.HasValue)
        {
            builder.Append(CultureInfo.InvariantCulture, $" Y{y.Value:0.###}");
        }

        if (z.HasValue)
        {
            builder.Append(CultureInfo.InvariantCulture, $" Z{z.Value:0.###}");
        }

        if (a.HasValue)
        {
            builder.Append(CultureInfo.InvariantCulture, $" A{a.Value:0.###}");
        }

        if (b.HasValue)
        {
            builder.Append(CultureInfo.InvariantCulture, $" B{b.Value:0.###}");
        }

        builder.Append(CultureInfo.InvariantCulture, $" F{feedRateMillimetersPerMinute.Value:0.###}");
        return SendCommandAsync(builder.ToString(), cancellationToken);
    }

    public Task SetWorkCoordinateOffsetAsync(
        double? x = null,
        double? y = null,
        double? z = null,
        double? a = null,
        double? b = null,
        CancellationToken cancellationToken = default)
    {
        var builder = new StringBuilder("G10 L20 P1");

        if (x.HasValue)
        {
            builder.Append(CultureInfo.InvariantCulture, $" X{x.Value:0.###}");
        }

        if (y.HasValue)
        {
            builder.Append(CultureInfo.InvariantCulture, $" Y{y.Value:0.###}");
        }

        if (z.HasValue)
        {
            builder.Append(CultureInfo.InvariantCulture, $" Z{z.Value:0.###}");
        }

        if (a.HasValue)
        {
            builder.Append(CultureInfo.InvariantCulture, $" A{a.Value:0.###}");
        }

        if (b.HasValue)
        {
            builder.Append(CultureInfo.InvariantCulture, $" B{b.Value:0.###}");
        }

        return SendCommandAsync(builder.ToString(), cancellationToken);
    }

    public Task MoveToMachineAsync(
        double? x = null,
        double? y = null,
        double? z = null,
        double? a = null,
        double? b = null,
        bool rapidMove = true,
        double? feedRateMillimetersPerMinute = null,
        CancellationToken cancellationToken = default)
    {
        if (!x.HasValue && !y.HasValue && !z.HasValue && !a.HasValue && !b.HasValue)
        {
            throw new ArgumentException("At least one machine axis target is required.");
        }

        var builder = new StringBuilder(rapidMove ? "G53 G0" : "G53 G1");

        if (x.HasValue)
        {
            builder.Append(CultureInfo.InvariantCulture, $" X{x.Value:0.###}");
        }

        if (y.HasValue)
        {
            builder.Append(CultureInfo.InvariantCulture, $" Y{y.Value:0.###}");
        }

        if (z.HasValue)
        {
            builder.Append(CultureInfo.InvariantCulture, $" Z{z.Value:0.###}");
        }

        if (a.HasValue)
        {
            builder.Append(CultureInfo.InvariantCulture, $" A{a.Value:0.###}");
        }

        if (b.HasValue)
        {
            builder.Append(CultureInfo.InvariantCulture, $" B{b.Value:0.###}");
        }

        if (!rapidMove)
        {
            if (!feedRateMillimetersPerMinute.HasValue || feedRateMillimetersPerMinute.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(feedRateMillimetersPerMinute), "A positive feed rate is required for machine feed moves.");
            }

            builder.Append(CultureInfo.InvariantCulture, $" F{feedRateMillimetersPerMinute.Value:0.###}");
        }

        return SendCommandAsync(builder.ToString(), cancellationToken);
    }

    public async Task ProbeAxisRelativeAsync(
        string axisLetter,
        double distanceMillimeters,
        double feedRateMillimetersPerMinute,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(axisLetter))
        {
            throw new ArgumentException("An axis letter is required.", nameof(axisLetter));
        }

        if (feedRateMillimetersPerMinute <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(feedRateMillimetersPerMinute), "A positive probe feed is required.");
        }

        var normalizedAxis = axisLetter.Trim().ToUpperInvariant();
        try
        {
            await SendCommandAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"G91 G38.2 {normalizedAxis}{distanceMillimeters:0.###} F{feedRateMillimetersPerMinute:0.###}"),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await SendCommandAsync("G90", cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    public Task FeedHoldAsync()
    {
        return SendRealtimeCommandAsync((byte)'!');
    }

    public Task HomeAsync(CancellationToken cancellationToken = default)
    {
        return SendCommandAsync("$H", cancellationToken);
    }

    public Task ResumeAsync()
    {
        return SendRealtimeCommandAsync((byte)'~');
    }

    public Task SoftResetAsync()
    {
        while (_pendingResponses.TryDequeue(out var pendingResponse))
        {
            pendingResponse.TrySetCanceled();
        }

        return SendRealtimeCommandAsync(0x18);
    }

    public Task UnlockAsync(CancellationToken cancellationToken = default)
    {
        return SendCommandAsync("$X", cancellationToken);
    }

    public Task SetSpindleSpeedAsync(int spindleSpeed, CancellationToken cancellationToken = default)
    {
        if (spindleSpeed < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spindleSpeed), "Spindle speed must be zero or greater.");
        }

        return spindleSpeed == 0
            ? StopSpindleAsync(cancellationToken)
            : SendCommandAsync(
                string.Create(CultureInfo.InvariantCulture, $"M3 S{spindleSpeed}"),
                cancellationToken);
    }

    public Task StopSpindleAsync(CancellationToken cancellationToken = default)
    {
        return SendCommandAsync("M5", cancellationToken);
    }

    public async Task SetFeedOverrideAsync(int targetPercent, int? currentPercent = null, CancellationToken cancellationToken = default)
    {
        var clampedTarget = Math.Clamp(targetPercent, 25, 200);
        var commands = BuildFeedOverrideCommands(clampedTarget, currentPercent);

        foreach (var command in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SendRealtimeCommandAsync(command).ConfigureAwait(false);
            await Task.Delay(5, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task StreamProgramAsync(
        IReadOnlyList<string> sourceLines,
        IProgress<(int completedLines, int totalLines)>? progress,
        CancellationToken cancellationToken)
    {
        var executableLines = new List<string>();
        foreach (var sourceLine in sourceLines)
        {
            var sanitizedLine = GCodeParser.SanitizeLine(sourceLine);
            if (!string.IsNullOrWhiteSpace(sanitizedLine))
            {
                executableLines.Add(sanitizedLine);
            }
        }

        for (var index = 0; index < executableLines.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SendCommandAsync(executableLines[index], cancellationToken).ConfigureAwait(false);
            progress?.Report((index + 1, executableLines.Count));
        }
    }

    public async Task<string> SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("A GRBL command is required.", nameof(command));
        }

        var responseCompletion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingResponses.Enqueue(responseCompletion);

        try
        {
            WriteLine(command);
        }
        catch
        {
            responseCompletion.TrySetCanceled();
            throw;
        }

        using var registration = cancellationToken.Register(() => responseCompletion.TrySetCanceled(cancellationToken));
        var response = await responseCompletion.Task.ConfigureAwait(false);

        if (response.StartsWith("error", StringComparison.OrdinalIgnoreCase) ||
            response.StartsWith("ALARM", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{command} failed: {response}");
        }

        return response;
    }

    public void Dispose()
    {
        _ = DisconnectAsync();
    }

    private Task SendRealtimeCommandAsync(byte command)
    {
        var serialPort = _serialPort ?? throw new InvalidOperationException("Not connected to a GRBL controller.");

        lock (_portLock)
        {
            serialPort.BaseStream.WriteByte(command);
            serialPort.BaseStream.Flush();
        }

        return Task.CompletedTask;
    }

    private async Task RunStatusPollingAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                var serialPort = _serialPort;
                if (serialPort is null || !serialPort.IsOpen)
                {
                    break;
                }

                lock (_portLock)
                {
                    serialPort.Write("?");
                }
            }
            catch (InvalidOperationException)
            {
                break;
            }
            catch (Exception exception)
            {
                MessageReceived?.Invoke(this, $"Status polling failed: {exception.Message}");
                break;
            }
        }
    }

    private void OnSerialPortDataReceived(object? sender, SerialDataReceivedEventArgs e)
    {
        var serialPort = _serialPort;
        if (serialPort is null)
        {
            return;
        }

        try
        {
            var incomingText = serialPort.ReadExisting();
            if (string.IsNullOrEmpty(incomingText))
            {
                return;
            }

            lock (_receiveBufferLock)
            {
                _receiveBuffer.Append(incomingText);
                ProcessBufferedLines();
            }
        }
        catch (Exception exception)
        {
            MessageReceived?.Invoke(this, $"Serial read failed: {exception.Message}");
        }
    }

    private void OnSerialPortErrorReceived(object? sender, SerialErrorReceivedEventArgs e)
    {
        MessageReceived?.Invoke(this, $"Serial error: {e.EventType}");
    }

    private void ProcessBufferedLines()
    {
        while (true)
        {
            var newlineIndex = FindNextNewLineIndex(_receiveBuffer);
            if (newlineIndex < 0)
            {
                break;
            }

            var line = _receiveBuffer.ToString(0, newlineIndex).Trim();
            _receiveBuffer.Remove(0, newlineIndex + 1);

            if (!string.IsNullOrWhiteSpace(line))
            {
                ProcessIncomingLine(line);
            }
        }
    }

    private void ProcessIncomingLine(string line)
    {
        if (line.StartsWith('<') && line.EndsWith('>') && GrblStatusParser.TryParse(line, out var status))
        {
            StatusReceived?.Invoke(this, status);
            return;
        }

        if (line.Equals("ok", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("error", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("ALARM", StringComparison.OrdinalIgnoreCase))
        {
            CompletePendingResponse(line);
            if (!line.Equals("ok", StringComparison.OrdinalIgnoreCase))
            {
                MessageReceived?.Invoke(this, line);
            }

            return;
        }

        if (line.StartsWith("[", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("GRBL", StringComparison.OrdinalIgnoreCase))
        {
            MessageReceived?.Invoke(this, line);
        }
    }

    private void CompletePendingResponse(string response)
    {
        while (_pendingResponses.TryDequeue(out var pendingResponse))
        {
            if (pendingResponse.TrySetResult(response))
            {
                return;
            }
        }
    }

    private void WriteLine(string command)
    {
        var serialPort = _serialPort ?? throw new InvalidOperationException("Not connected to a GRBL controller.");

        lock (_portLock)
        {
            serialPort.WriteLine(command);
        }
    }

    private static int FindNextNewLineIndex(StringBuilder buffer)
    {
        for (var index = 0; index < buffer.Length; index++)
        {
            if (buffer[index] == '\n')
            {
                return index;
            }
        }

        return -1;
    }

    private static IReadOnlyList<byte> BuildFeedOverrideCommands(int targetPercent, int? currentPercent)
    {
        var commands = new List<byte>();
        var current = currentPercent.HasValue
            ? Math.Clamp(currentPercent.Value, 10, 200)
            : 100;

        if (!currentPercent.HasValue || current != 100)
        {
            commands.Add(0x90);
            current = 100;
        }

        while (current + 10 <= targetPercent)
        {
            commands.Add(0x91);
            current += 10;
        }

        while (current - 10 >= targetPercent)
        {
            commands.Add(0x92);
            current -= 10;
        }

        while (current < targetPercent)
        {
            commands.Add(0x93);
            current++;
        }

        while (current > targetPercent)
        {
            commands.Add(0x94);
            current--;
        }

        return commands;
    }
}
