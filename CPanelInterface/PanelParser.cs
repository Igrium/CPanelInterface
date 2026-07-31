using System.Collections.Concurrent;

namespace CPanelInterface;



public class PanelParser
{
    public delegate void ValueUpdateListener(byte row, byte[] value);

    public event ValueUpdateListener? OnUpdateValue;

    public PanelTransport.Listener Listener { get; init; }
    public PanelTransport Transport => Listener.Transport;
    

    private readonly ConcurrentDictionary<byte, byte[]> _values = new();

    public PanelParser(PanelTransport transport) : this(new PanelTransport.Listener(transport)) { }
    public PanelParser(PanelTransport.Listener listener)
    {
        Listener = listener;
        Listener.OnMessage += OnMessage;
    }

    private void OnMessage(string message)
    {
        try
        {
            var bytes = Convert.FromHexString(message);
            if (bytes.Length < 2) return;

            byte[] value = bytes.Skip(1).ToArray();
            _values[bytes[0]] = value;
            OnUpdateValue?.Invoke(bytes[0], value);
        }
        catch (FormatException e)
        {
            Console.WriteLine(e);
        }
    }

    public byte[]? GetByteValues(byte row)
    {
        return !_values.TryGetValue(row, out var bytes) ? bytes : null;
    }

    public bool GetByteValue(byte row, out byte value)
    {
        bool success = _values.TryGetValue(row, out var bytes);
        if (success && bytes?.Length >= 1)
        {
            value = bytes[0];
            return true;
        }
        // For some reason, control panel defaults to all high
        value = 0xFF;
        return false;
    }

    public byte GetByteValue(byte row)
    {
        GetByteValue(row, out var value);
        return value;
    }

    public float GetFloatValue(byte row)
    {
        return GetByteValue(row) / 255f;
    }

    public bool GetJoysickValue(byte row, out JoystickPos pos)
    {
        bool success = _values.TryGetValue(row, out var bytes);
        pos = success ? JoystickPos.Of(bytes!) : default;
        return success;
    }

    public bool GetButtonState(byte row, int button)
    {
        return !GetBitAsBool(GetByteValue(row), button);
    }
    
    private static bool GetBitAsBool(byte value, int bitIndex)
    {
        return ((value >> bitIndex) & 1) == 1;
    }
}