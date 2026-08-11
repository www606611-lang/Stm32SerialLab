using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace Stm32SerialLab.Models;

public enum SerialDirection
{
    Receive,
    Transmit,
    System,
    Demo
}

public sealed class SerialLogEntry : INotifyPropertyChanged
{
    private string _displayText;

    public SerialLogEntry(DateTimeOffset timestamp, SerialDirection direction, byte[] data, string? textOverride = null)
    {
        Timestamp = timestamp;
        Direction = direction;
        Data = data;
        TextOverride = textOverride;
        _displayText = textOverride ?? FormatAscii(data);
    }

    public DateTimeOffset Timestamp { get; }
    public SerialDirection Direction { get; }
    public byte[] Data { get; }
    public string? TextOverride { get; }
    public int ByteCount => Data.Length;
    public string AsciiText => TextOverride ?? FormatAscii(Data);
    public string HexText => TextOverride ?? FormatHex(Data);
    public string TimestampText => Timestamp.ToString("HH:mm:ss.fff");
    public string DirectionText => Direction switch
    {
        SerialDirection.Receive => "RX",
        SerialDirection.Transmit => "TX",
        SerialDirection.Demo => "SIM",
        _ => "SYS"
    };

    public SolidColorBrush DirectionBrush => Direction switch
    {
        SerialDirection.Receive => new SolidColorBrush(ColorHelper.FromArgb(255, 15, 118, 110)),
        SerialDirection.Transmit => new SolidColorBrush(ColorHelper.FromArgb(255, 37, 99, 235)),
        SerialDirection.Demo => new SolidColorBrush(ColorHelper.FromArgb(255, 147, 51, 234)),
        _ => new SolidColorBrush(ColorHelper.FromArgb(255, 107, 114, 128))
    };

    public string DisplayText
    {
        get => _displayText;
        private set
        {
            if (_displayText == value)
            {
                return;
            }

            _displayText = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetHexDisplay(bool useHex)
    {
        DisplayText = useHex ? HexText : AsciiText;
    }

    private static string FormatHex(byte[] data)
    {
        return Convert.ToHexString(data).Chunk(2).Select(chars => new string(chars))
            .Aggregate(string.Empty, (left, right) => left.Length == 0 ? right : $"{left} {right}");
    }

    private static string FormatAscii(IEnumerable<byte> bytes)
    {
        StringBuilder builder = new();
        foreach (byte value in bytes)
        {
            builder.Append(value switch
            {
                0x00 => "\\0",
                0x09 => "\\t",
                0x0A => "\\n",
                0x0D => "\\r",
                >= 0x20 and <= 0x7E => ((char)value).ToString(),
                _ => $"\\x{value:X2}"
            });
        }

        return builder.ToString();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
