using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;

namespace Stm32SerialLab.Models;

public readonly record struct TelemetrySample(DateTimeOffset Timestamp, double Value);

public sealed class TelemetryMetric : INotifyPropertyChanged
{
    private const int MaxSamples = 6000;
    private bool _isVisible;

    public TelemetryMetric(string name, SolidColorBrush stroke, bool isVisible)
    {
        Name = name;
        Stroke = stroke;
        _isVisible = isVisible;
    }

    public string Name { get; }
    public SolidColorBrush Stroke { get; }
    public Queue<TelemetrySample> Samples { get; } = new();
    public double Latest { get; private set; }
    public double Minimum { get; private set; }
    public double Maximum { get; private set; }
    public long SampleCount { get; private set; }
    public string LatestText => Latest.ToString("0.###");
    public string MinimumText => Minimum.ToString("0.###");
    public string MaximumText => Maximum.ToString("0.###");
    public string SampleCountText => SampleCount.ToString("N0");

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value)
            {
                return;
            }

            _isVisible = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void AddSample(DateTimeOffset timestamp, double value)
    {
        Latest = value;
        if (SampleCount == 0)
        {
            Minimum = value;
            Maximum = value;
        }
        else
        {
            Minimum = Math.Min(Minimum, value);
            Maximum = Math.Max(Maximum, value);
        }

        SampleCount++;
        Samples.Enqueue(new TelemetrySample(timestamp, value));
        while (Samples.Count > MaxSamples)
        {
            Samples.Dequeue();
        }

        OnPropertyChanged(nameof(LatestText));
        OnPropertyChanged(nameof(MinimumText));
        OnPropertyChanged(nameof(MaximumText));
        OnPropertyChanged(nameof(SampleCountText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
