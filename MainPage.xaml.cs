using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Stm32SerialLab.Models;
using Stm32SerialLab.Services;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace Stm32SerialLab;

public sealed partial class MainPage : Page, INotifyPropertyChanged
{
    private const int MaxLogEntries = 5000;
    private const int MaxLineBytes = 2048;
    private static readonly UTF8Encoding Utf8 = new(false, false);
    private static readonly string[] Palette =
    [
        "#0F766E", "#2563EB", "#DC2626", "#9333EA", "#CA8A04", "#0891B2", "#DB2777", "#4D7C0F"
    ];
    private static readonly double[] TimeWindowSteps = [0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 30, 60, 120, 300];
    private static readonly double[] VerticalGainSteps = [0.0625, 0.125, 0.25, 0.5, 1, 2, 4, 8, 16, 32, 64];

    private readonly SerialPortService _serialPort = new();
    private readonly TelemetryParser _telemetryParser = new();
    private readonly List<SerialLogEntry> _allLogs = [];
    private readonly List<byte> _lineBuffer = [];
    private readonly Dictionary<string, TelemetryMetric> _metricsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _commandHistory = [];
    private readonly DispatcherTimer _demoTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _plotTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly Stopwatch _demoClock = new();
    private readonly Random _random = new();
    private bool _loaded;
    private bool _displayHex;
    private bool _scopeHeld;
    private bool _panArmed;
    private bool _isPanning;
    private int _historyIndex;
    private double _timeWindowSeconds = 10;
    private double _verticalGain = 1;
    private double _panStartX;
    private DateTimeOffset _scopeEndTime;
    private DateTimeOffset _panStartEndTime;
    private double _demoAverage = 1870;
    private long _rxBytes;
    private long _txBytes;
    private long _rxLines;
    private long _parseErrors;
    private long _connectionErrors;

    public MainPage()
    {
        InitializeComponent();
        _serialPort.BytesReceived += SerialPort_BytesReceived;
        _serialPort.PortError += SerialPort_PortError;
        _demoTimer.Tick += DemoTimer_Tick;
        _plotTimer.Tick += PlotTimer_Tick;
    }

    public ObservableCollection<string> PortNames { get; } = [];
    public ObservableCollection<SerialLogEntry> Logs { get; } = [];
    public ObservableCollection<TelemetryMetric> Metrics { get; } = [];
    public string RxStatusText => $"RX {_rxBytes:N0} B";
    public string TxStatusText => $"TX {_txBytes:N0} B";
    public string LineStatusText => $"LINES {_rxLines:N0}";
    public string ErrorStatusText => $"ERR {_parseErrors + _connectionErrors:N0}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        DisplayModeSelector.SelectedItem = AsciiModeItem;
        RefreshPorts();
        _plotTimer.Start();
        DemoToggle.IsChecked = true;
        SetDemoMode(true);
        SendTextBox.Focus(FocusState.Programmatic);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _plotTimer.Stop();
        _demoTimer.Stop();
        _serialPort.Close();
    }

    private void RefreshPorts_Click(object sender, RoutedEventArgs e)
    {
        RefreshPorts();
    }

    private void RefreshPorts()
    {
        string? previous = PortComboBox.SelectedItem as string;
        PortNames.Clear();
        foreach (string portName in SerialPortService.GetPortNames())
        {
            PortNames.Add(portName);
        }

        if (previous is not null && PortNames.Contains(previous))
        {
            PortComboBox.SelectedItem = previous;
        }
        else if (PortNames.Count > 0)
        {
            PortComboBox.SelectedIndex = 0;
        }

        TransientStatusText.Text = PortNames.Count == 0 ? "No COM ports" : $"{PortNames.Count} port(s)";
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_serialPort.IsOpen)
        {
            _serialPort.Close();
            SetConnectionState(false, "DISCONNECTED");
            AddSystemLog("Port closed");
            return;
        }

        if (PortComboBox.SelectedItem is not string portName ||
            BaudComboBox.SelectedItem is not string baudText ||
            !int.TryParse(baudText, out int baudRate))
        {
            TransientStatusText.Text = "Select a COM port";
            return;
        }

        try
        {
            DemoToggle.IsChecked = false;
            SetDemoMode(false);
            _serialPort.Open(portName, baudRate);
            SetConnectionState(true, $"{portName} @ {baudRate}");
            AddSystemLog($"Opened {portName}, {baudRate} baud, 8-N-1");
        }
        catch (Exception exception)
        {
            _connectionErrors++;
            SetConnectionState(false, "CONNECT ERROR");
            AddSystemLog(exception.Message);
            RefreshCounters();
        }
    }

    private void DemoToggle_Click(object sender, RoutedEventArgs e)
    {
        SetDemoMode(DemoToggle.IsChecked == true);
    }

    private void SetDemoMode(bool enabled)
    {
        if (enabled)
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }

            if (!_demoTimer.IsEnabled)
            {
                _demoClock.Restart();
                _demoTimer.Start();
                AddSystemLog("Demo telemetry started");
            }

            SetConnectionState(false, "DEMO");
            ConnectionDot.Fill = new SolidColorBrush(ColorHelper.FromArgb(255, 147, 51, 234));
        }
        else
        {
            _demoTimer.Stop();
            if (!_serialPort.IsOpen)
            {
                SetConnectionState(false, "DISCONNECTED");
            }
        }
    }

    private void DemoTimer_Tick(object? sender, object e)
    {
        long tick = _demoClock.ElapsedMilliseconds;
        double phase = tick / 900.0;
        int adc = (int)Math.Round(1870 + Math.Sin(phase) * 210 + Math.Sin(phase * 0.23) * 55 + _random.Next(-8, 9));
        _demoAverage = (_demoAverage * 0.88) + (adc * 0.12);
        int heap = 4224 - (int)((tick / 10000) % 4) * 16;
        string line = FormattableString.Invariant($"tick={tick} heap={heap} adc={adc} avg={_demoAverage:F1} overrun=0\r\n");
        ProcessReceivedBytes(Encoding.ASCII.GetBytes(line), SerialDirection.Demo);
    }

    private void SerialPort_BytesReceived(object? sender, byte[] data)
    {
        DispatcherQueue.TryEnqueue(() => ProcessReceivedBytes(data, SerialDirection.Receive));
    }

    private void SerialPort_PortError(object? sender, string message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _connectionErrors++;
            AddSystemLog(message);
            RefreshCounters();
        });
    }

    private void ProcessReceivedBytes(byte[] data, SerialDirection direction)
    {
        _rxBytes += data.Length;
        AddLog(new SerialLogEntry(DateTimeOffset.Now, direction, data));

        foreach (byte value in data)
        {
            if (value == (byte)'\n')
            {
                _rxLines++;
                string line = Utf8.GetString(_lineBuffer.ToArray()).TrimEnd('\r');
                _lineBuffer.Clear();
                ProcessTelemetryLine(line);
                continue;
            }

            _lineBuffer.Add(value);
            if (_lineBuffer.Count <= MaxLineBytes)
            {
                continue;
            }

            _lineBuffer.Clear();
            _parseErrors++;
            AddSystemLog($"Line exceeded {MaxLineBytes} bytes and was discarded");
        }

        RefreshCounters();
    }

    private void ProcessTelemetryLine(string line)
    {
        TelemetryParseResult result = _telemetryParser.Parse(line);
        if (result.IsError)
        {
            _parseErrors++;
            return;
        }

        if (!result.IsTelemetry || result.IsHeader)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.Now;
        foreach ((string name, double value) in result.Values)
        {
            GetOrCreateMetric(name).AddSample(now, value);
        }
    }

    private TelemetryMetric GetOrCreateMetric(string name)
    {
        if (_metricsByName.TryGetValue(name, out TelemetryMetric? metric))
        {
            return metric;
        }

        string color = Palette[Metrics.Count % Palette.Length];
        bool suppressedByDefault = name.Equals("tick", StringComparison.OrdinalIgnoreCase) ||
                                   name.Equals("heap", StringComparison.OrdinalIgnoreCase) ||
                                   name.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                                   name.Contains("overrun", StringComparison.OrdinalIgnoreCase);
        bool preferred = name.Equals("adc", StringComparison.OrdinalIgnoreCase) || name.Equals("avg", StringComparison.OrdinalIgnoreCase);
        bool visible = preferred || (!suppressedByDefault && Metrics.Count(item => item.IsVisible) < 2);
        metric = new TelemetryMetric(name, new SolidColorBrush(ParseColor(color)), visible);
        _metricsByName.Add(name, metric);
        Metrics.Add(metric);
        return metric;
    }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        SendCurrentText();
    }

    private void SendTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            SendCurrentText();
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.Up)
        {
            NavigateHistory(-1);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Down)
        {
            NavigateHistory(1);
            e.Handled = true;
        }
    }

    private void SendCurrentText()
    {
        string input = SendTextBox.Text;
        if (string.IsNullOrEmpty(input))
        {
            return;
        }

        byte[] bytes;
        try
        {
            bytes = SendModeComboBox.SelectedIndex == 1 ? ParseHexBytes(input) : BuildTextBytes(input);
        }
        catch (FormatException exception)
        {
            _parseErrors++;
            TransientStatusText.Text = exception.Message;
            RefreshCounters();
            return;
        }

        try
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.Write(bytes);
            }
            else if (DemoToggle.IsChecked != true)
            {
                TransientStatusText.Text = "Not connected";
                return;
            }

            _txBytes += bytes.Length;
            AddLog(new SerialLogEntry(DateTimeOffset.Now, SerialDirection.Transmit, bytes));
            RefreshCounters();
            RememberCommand(input);
            SendTextBox.Text = string.Empty;
        }
        catch (Exception exception)
        {
            _connectionErrors++;
            AddSystemLog(exception.Message);
            RefreshCounters();
        }
    }

    private byte[] BuildTextBytes(string input)
    {
        string ending = LineEndingComboBox.SelectedIndex switch
        {
            1 => "\n",
            2 => "\r\n",
            3 => "\r",
            _ => string.Empty
        };
        return Utf8.GetBytes(input + ending);
    }

    private static byte[] ParseHexBytes(string input)
    {
        string compact = input.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace(":", string.Empty)
            .Replace(",", string.Empty);
        if (compact.Length == 0 || compact.Length % 2 != 0 || compact.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new FormatException("HEX requires complete byte pairs");
        }

        return Convert.FromHexString(compact);
    }

    private void RememberCommand(string command)
    {
        if (_commandHistory.Count == 0 || _commandHistory[^1] != command)
        {
            _commandHistory.Add(command);
            if (_commandHistory.Count > 100)
            {
                _commandHistory.RemoveAt(0);
            }
        }

        _historyIndex = _commandHistory.Count;
    }

    private void NavigateHistory(int delta)
    {
        if (_commandHistory.Count == 0)
        {
            return;
        }

        _historyIndex = Math.Clamp(_historyIndex + delta, 0, _commandHistory.Count);
        SendTextBox.Text = _historyIndex == _commandHistory.Count ? string.Empty : _commandHistory[_historyIndex];
        SendTextBox.SelectionStart = SendTextBox.Text.Length;
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (PauseButton.IsChecked == true)
        {
            TransientStatusText.Text = "Timeline paused";
            return;
        }

        Logs.Clear();
        foreach (SerialLogEntry entry in _allLogs)
        {
            Logs.Add(entry);
        }

        EmptyLogText.Visibility = Logs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ScrollToLatest();
        TransientStatusText.Text = "Timeline resumed";
    }

    private void DisplayModeSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        _displayHex = sender.SelectedItem == HexModeItem;
        foreach (SerialLogEntry entry in _allLogs)
        {
            entry.SetHexDisplay(_displayHex);
        }
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        _allLogs.Clear();
        Logs.Clear();
        _lineBuffer.Clear();
        Metrics.Clear();
        _metricsByName.Clear();
        _rxBytes = 0;
        _txBytes = 0;
        _rxLines = 0;
        _parseErrors = 0;
        _connectionErrors = 0;
        EmptyLogText.Visibility = Visibility.Visible;
        RefreshCounters();
        RenderPlot();
        TransientStatusText.Text = "Cleared";
    }

    private async void ExportLog_Click(object sender, RoutedEventArgs e)
    {
        FileSavePicker picker = CreateSavePicker("stm32-serial.log", "Serial log", ".log");
        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        StringBuilder output = new();
        foreach (SerialLogEntry entry in _allLogs)
        {
            output.Append(entry.Timestamp.ToString("O", CultureInfo.InvariantCulture))
                .Append('\t').Append(entry.DirectionText)
                .Append('\t').Append(entry.ByteCount)
                .Append("\tHEX=").Append(entry.HexText)
                .Append("\tASCII=").AppendLine(entry.AsciiText);
        }

        await FileIO.WriteTextAsync(file, output.ToString());
        TransientStatusText.Text = $"Saved {file.Name}";
    }

    private async void ExportTelemetry_Click(object sender, RoutedEventArgs e)
    {
        FileSavePicker picker = CreateSavePicker("stm32-telemetry.csv", "Telemetry CSV", ".csv");
        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        StringBuilder output = new("timestamp,channel,value\r\n");
        foreach (TelemetryMetric metric in Metrics)
        {
            foreach (TelemetrySample sample in metric.Samples)
            {
                output.Append(sample.Timestamp.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                    .Append(EscapeCsv(metric.Name)).Append(',')
                    .Append(sample.Value.ToString("R", CultureInfo.InvariantCulture)).Append("\r\n");
            }
        }

        await FileIO.WriteTextAsync(file, output.ToString());
        TransientStatusText.Text = $"Saved {file.Name}";
    }

    private static string EscapeCsv(string value)
    {
        return value.ContainsAny([',', '"', '\r', '\n']) ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    private static FileSavePicker CreateSavePicker(string suggestedName, string description, string extension)
    {
        FileSavePicker picker = new()
        {
            SuggestedFileName = suggestedName
        };
        picker.FileTypeChoices.Add(description, [extension]);

        if (Application.Current is App { MainWindow: not null } app)
        {
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(app.MainWindow));
        }

        return picker;
    }

    private void AddSystemLog(string message)
    {
        AddLog(new SerialLogEntry(DateTimeOffset.Now, SerialDirection.System, [], message));
        TransientStatusText.Text = message;
    }

    private void AddLog(SerialLogEntry entry)
    {
        entry.SetHexDisplay(_displayHex);
        _allLogs.Add(entry);
        if (_allLogs.Count > MaxLogEntries)
        {
            _allLogs.RemoveAt(0);
        }

        if (PauseButton.IsChecked == true)
        {
            return;
        }

        Logs.Add(entry);
        if (Logs.Count > MaxLogEntries)
        {
            Logs.RemoveAt(0);
        }

        EmptyLogText.Visibility = Visibility.Collapsed;
        ScrollToLatest();
    }

    private void ScrollToLatest()
    {
        if (AutoScrollButton.IsChecked == true && Logs.Count > 0)
        {
            LogListView.ScrollIntoView(Logs[^1], ScrollIntoViewAlignment.Leading);
        }
    }

    private void SetConnectionState(bool connected, string text)
    {
        ConnectionStatusText.Text = text;
        ConnectionDot.Fill = new SolidColorBrush(connected
            ? ColorHelper.FromArgb(255, 22, 163, 74)
            : ColorHelper.FromArgb(255, 107, 114, 128));
        ConnectButton.Content = new SymbolIcon(connected ? Symbol.Stop : Symbol.Play);
        TransientStatusText.Text = text;
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        RootLayout.RequestedTheme = RootLayout.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        RenderPlot();
    }

    private void PlotTimer_Tick(object? sender, object e)
    {
        RenderPlot();
    }

    private void ScopeCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderPlot();
    }

    private void TimeWindowSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        int index = Math.Clamp((int)Math.Round(e.NewValue), 0, TimeWindowSteps.Length - 1);
        _timeWindowSeconds = TimeWindowSteps[index];
        if (TimeWindowText is not null)
        {
            TimeWindowText.Text = FormatTimeWindow(_timeWindowSeconds);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(TimeWindowSlider, $"Scope time window {TimeWindowText.Text}");
        }

        RenderPlot();
    }

    private void VerticalGainSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        int index = Math.Clamp((int)Math.Round(e.NewValue), 0, VerticalGainSteps.Length - 1);
        _verticalGain = VerticalGainSteps[index];
        if (VerticalGainText is not null)
        {
            VerticalGainText.Text = FormatVerticalGain(_verticalGain);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(VerticalGainSlider, $"Scope vertical gain {VerticalGainText.Text}");
        }

        RenderPlot();
    }

    private void ScopeHoldButton_Click(object sender, RoutedEventArgs e)
    {
        if (ScopeHoldButton.IsChecked == true)
        {
            HoldScopeAt(DateTimeOffset.Now);
        }
        else
        {
            GoToLiveScope();
        }
    }

    private void ScopeLiveButton_Click(object sender, RoutedEventArgs e)
    {
        GoToLiveScope();
    }

    private void ScopeResetButton_Click(object sender, RoutedEventArgs e)
    {
        TimeWindowSlider.Value = 6;
        VerticalGainSlider.Value = 4;
        GoToLiveScope();
    }

    private void ScopeCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        int delta = e.GetCurrentPoint(ScopeCanvas).Properties.MouseWheelDelta;
        if ((e.KeyModifiers & VirtualKeyModifiers.Control) != 0)
        {
            double step = delta > 0 ? 1 : -1;
            VerticalGainSlider.Value = Math.Clamp(VerticalGainSlider.Value + step, VerticalGainSlider.Minimum, VerticalGainSlider.Maximum);
        }
        else
        {
            double step = delta > 0 ? -1 : 1;
            TimeWindowSlider.Value = Math.Clamp(TimeWindowSlider.Value + step, TimeWindowSlider.Minimum, TimeWindowSlider.Maximum);
        }

        e.Handled = true;
    }

    private void ScopeCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(ScopeCanvas);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _panArmed = true;
        _isPanning = false;
        _panStartX = point.Position.X;
        _panStartEndTime = _scopeHeld ? _scopeEndTime : DateTimeOffset.Now;
        ScopeCanvas.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ScopeCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_panArmed || ScopeCanvas.ActualWidth <= 0)
        {
            return;
        }

        double deltaX = e.GetCurrentPoint(ScopeCanvas).Position.X - _panStartX;
        if (!_isPanning)
        {
            if (Math.Abs(deltaX) < 4)
            {
                return;
            }

            _isPanning = true;
            if (!_scopeHeld)
            {
                HoldScopeAt(_panStartEndTime);
            }
        }

        DateTimeOffset candidate = _panStartEndTime.AddSeconds(-(deltaX / ScopeCanvas.ActualWidth) * _timeWindowSeconds);
        _scopeEndTime = candidate > DateTimeOffset.Now ? DateTimeOffset.Now : candidate;
        RenderPlot();
        e.Handled = true;
    }

    private void ScopeCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _panArmed = false;
        _isPanning = false;
        ScopeCanvas.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void ScopeCanvas_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _panArmed = false;
        _isPanning = false;
    }

    private void HoldScopeAt(DateTimeOffset endTime)
    {
        _scopeHeld = true;
        _scopeEndTime = endTime;
        ScopeHoldButton.IsChecked = true;
        ScopeModeText.Text = "HOLD";
        TransientStatusText.Text = "Scope held";
        RenderPlot();
    }

    private void GoToLiveScope()
    {
        _scopeHeld = false;
        ScopeHoldButton.IsChecked = false;
        ScopeModeText.Text = "LIVE";
        TransientStatusText.Text = "Scope live";
        RenderPlot();
    }

    private void WorkspaceTabs_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double contentWidth = Math.Max(0, e.NewSize.Width - 2);
        double contentHeight = Math.Max(120, e.NewSize.Height - 48);
        ConsolePanel.Width = contentWidth;
        ConsolePanel.Height = contentHeight;
        ScopePanel.Width = contentWidth;
        ScopePanel.Height = contentHeight;
        TelemetryPanel.Width = contentWidth;
        TelemetryPanel.Height = contentHeight;
    }

    private void RenderPlot()
    {
        if (ScopeCanvas is null || ScopeCanvas.ActualWidth < 160 || ScopeCanvas.ActualHeight < 120)
        {
            return;
        }

        ScopeCanvas.Children.Clear();
        TelemetryMetric[] selected = Metrics.Where(metric => metric.IsVisible && metric.Samples.Count > 1).ToArray();

        double width = ScopeCanvas.ActualWidth;
        double height = ScopeCanvas.ActualHeight;
        const double left = 54;
        const double right = 16;
        const double top = 18;
        const double bottom = 32;
        double plotWidth = Math.Max(1, width - left - right);
        double plotHeight = Math.Max(1, height - top - bottom);
        Windows.UI.Color gridColor = RootLayout.ActualTheme == ElementTheme.Dark
            ? ColorHelper.FromArgb(70, 255, 255, 255)
            : ColorHelper.FromArgb(45, 0, 0, 0);
        SolidColorBrush gridBrush = new(gridColor);
        SolidColorBrush labelBrush = new(RootLayout.ActualTheme == ElementTheme.Dark
            ? ColorHelper.FromArgb(190, 255, 255, 255)
            : ColorHelper.FromArgb(175, 0, 0, 0));

        for (int index = 0; index <= 5; index++)
        {
            double x = left + (plotWidth * index / 5.0);
            double y = top + (plotHeight * index / 5.0);
            ScopeCanvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = top, Y2 = top + plotHeight, Stroke = gridBrush, StrokeThickness = 1 });
            ScopeCanvas.Children.Add(new Line { X1 = left, X2 = left + plotWidth, Y1 = y, Y2 = y, Stroke = gridBrush, StrokeThickness = 1 });
        }

        double windowSeconds = _timeWindowSeconds;
        DateTimeOffset maxTime = _scopeHeld ? _scopeEndTime : DateTimeOffset.Now;
        DateTimeOffset minTime = maxTime.AddSeconds(-windowSeconds);
        Dictionary<TelemetryMetric, TelemetrySample[]> visibleSamples = selected.ToDictionary(
            metric => metric,
            metric => metric.Samples.Where(sample => sample.Timestamp >= minTime && sample.Timestamp <= maxTime).ToArray());
        TelemetrySample[] allSamples = visibleSamples.Values.SelectMany(samples => samples).ToArray();
        PlotEmptyText.Visibility = allSamples.Length < 2 ? Visibility.Visible : Visibility.Collapsed;
        if (allSamples.Length < 2)
        {
            return;
        }

        double minValue = allSamples.Min(sample => sample.Value);
        double maxValue = allSamples.Max(sample => sample.Value);
        if (Math.Abs(maxValue - minValue) < 1e-9)
        {
            minValue -= 1;
            maxValue += 1;
        }

        double padding = (maxValue - minValue) * 0.08;
        minValue -= padding;
        maxValue += padding;
        double center = (minValue + maxValue) / 2;
        double halfSpan = (maxValue - minValue) / (2 * _verticalGain);
        minValue = center - halfSpan;
        maxValue = center + halfSpan;
        double milliseconds = windowSeconds * 1000;

        AddCanvasLabel(maxValue.ToString("0.###", CultureInfo.InvariantCulture), 4, top - 7, labelBrush);
        AddCanvasLabel(minValue.ToString("0.###", CultureInfo.InvariantCulture), 4, top + plotHeight - 8, labelBrush);
        AddCanvasLabel($"-{FormatTimeWindow(windowSeconds)}", left, top + plotHeight + 8, labelBrush);
        AddCanvasLabel("now", left + plotWidth - 24, top + plotHeight + 8, labelBrush);

        foreach (TelemetryMetric metric in selected)
        {
            PointCollection points = [];
            foreach (TelemetrySample sample in visibleSamples[metric])
            {
                double x = left + ((sample.Timestamp - minTime).TotalMilliseconds / milliseconds * plotWidth);
                double y = top + ((maxValue - sample.Value) / (maxValue - minValue) * plotHeight);
                points.Add(new Point(x, y));
            }

            ScopeCanvas.Children.Add(new Polyline
            {
                Points = points,
                Stroke = metric.Stroke,
                StrokeThickness = 1.8,
                StrokeLineJoin = PenLineJoin.Round,
                Clip = new RectangleGeometry { Rect = new Rect(left, top, plotWidth, plotHeight) }
            });
        }
    }

    private void AddCanvasLabel(string text, double x, double y, Brush foreground)
    {
        TextBlock label = new()
        {
            Text = text,
            FontFamily = (FontFamily)Application.Current.Resources["LabMonoFont"],
            FontSize = 10,
            Foreground = foreground
        };
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        ScopeCanvas.Children.Add(label);
    }

    private static string FormatTimeWindow(double seconds)
    {
        return seconds < 1 ? $"{seconds * 1000:0} ms" : $"{seconds:0.#} s";
    }

    private static string FormatVerticalGain(double gain)
    {
        return gain switch
        {
            0.0625 => "1/16x",
            0.125 => "1/8x",
            0.25 => "1/4x",
            0.5 => "1/2x",
            _ => $"{gain:0}x"
        };
    }

    private void RefreshCounters()
    {
        OnPropertyChanged(nameof(RxStatusText));
        OnPropertyChanged(nameof(TxStatusText));
        OnPropertyChanged(nameof(LineStatusText));
        OnPropertyChanged(nameof(ErrorStatusText));
    }

    private static Windows.UI.Color ParseColor(string value)
    {
        return ColorHelper.FromArgb(
            255,
            byte.Parse(value.AsSpan(1, 2), NumberStyles.HexNumber),
            byte.Parse(value.AsSpan(3, 2), NumberStyles.HexNumber),
            byte.Parse(value.AsSpan(5, 2), NumberStyles.HexNumber));
    }

    private void WorkspaceTabs_AddTabButtonClick(TabView sender, object args)
    {
    }

    private void WorkspaceTabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
