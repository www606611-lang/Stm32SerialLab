using System.IO.Ports;

namespace Stm32SerialLab.Services;

public sealed class SerialPortService : IDisposable
{
    private readonly object _gate = new();
    private SerialPort? _port;

    public event EventHandler<byte[]>? BytesReceived;
    public event EventHandler<string>? PortError;

    public bool IsOpen => _port?.IsOpen == true;
    public string? PortName => _port?.PortName;

    public static IReadOnlyList<string> GetPortNames()
    {
        return SerialPort.GetPortNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public void Open(string portName, int baudRate)
    {
        lock (_gate)
        {
            CloseCore();
            SerialPort port = new(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                ReadTimeout = 250,
                WriteTimeout = 1000,
                DtrEnable = false,
                RtsEnable = false
            };

            port.DataReceived += OnDataReceived;
            port.ErrorReceived += OnErrorReceived;
            port.Open();
            _port = port;
        }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        lock (_gate)
        {
            if (_port?.IsOpen != true)
            {
                throw new InvalidOperationException("Serial port is not open.");
            }

            byte[] copy = data.ToArray();
            _port.Write(copy, 0, copy.Length);
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            CloseCore();
        }
    }

    public void Dispose()
    {
        Close();
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs args)
    {
        try
        {
            if (sender is not SerialPort port || !port.IsOpen)
            {
                return;
            }

            int available = port.BytesToRead;
            if (available <= 0)
            {
                return;
            }

            byte[] buffer = new byte[available];
            int read = port.Read(buffer, 0, buffer.Length);
            if (read > 0)
            {
                BytesReceived?.Invoke(this, read == buffer.Length ? buffer : buffer[..read]);
            }
        }
        catch (Exception exception)
        {
            PortError?.Invoke(this, exception.Message);
        }
    }

    private void OnErrorReceived(object sender, SerialErrorReceivedEventArgs args)
    {
        PortError?.Invoke(this, $"Serial hardware error: {args.EventType}");
    }

    private void CloseCore()
    {
        if (_port is null)
        {
            return;
        }

        _port.DataReceived -= OnDataReceived;
        _port.ErrorReceived -= OnErrorReceived;
        if (_port.IsOpen)
        {
            _port.Close();
        }

        _port.Dispose();
        _port = null;
    }
}
