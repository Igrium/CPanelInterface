using System.Collections.Concurrent;

namespace CPanelInterface;



public class PanelParser
{
    /// <summary>
    /// Don't call button events on these
    /// </summary>
    public ISet<byte> Encoders { get; } = new HashSet<byte>();
    
    public delegate void ValueUpdateListener(byte row, byte[] value);

    public event ValueUpdateListener? OnUpdateValue;

    public delegate void ButtonPressListener(byte row, byte idx, bool pressed);
    
    public event ButtonPressListener? OnPressButton;

    public PanelTransport.Listener Listener { get; init; }
    public PanelTransport Transport => Listener.Transport;
    

    private readonly ConcurrentDictionary<byte, byte[]> _values = new();

    public PanelParser(PanelTransport transport) : this(new PanelTransport.Listener(transport)) { }
    public PanelParser(PanelTransport.Listener listener)
    {
        Listener = listener;
        Listener.OnMessage += OnMessage;

        Encoders.UnionWith([54, 55, 70, 71, 128, 144]);
    }

    private void OnMessage(string message)
    {
        try
        {
            var bytes = Convert.FromHexString(message);
            if (bytes.Length < 2) return;

            byte row = bytes[0];
            byte[] values = bytes.Skip(1).ToArray();
            byte[]? prevVals = _values.GetValueOrDefault(row);

            byte prevFlags = prevVals != null ? prevVals[0] : (byte)0xFF;
            
            if (!Encoders.Contains(row))
            {
                byte curFlags = values[0];

                for (byte i = 0; i < 8; i++)
                {
                    bool prev = GetBitAsBool(prevFlags, i);
                    bool cur = GetBitAsBool(curFlags, i);
                    if (prev != cur)
                    {
                        OnPressButton?.Invoke(row, i, !cur);
                    }
                }
            }
            
            _values[row] = values;
            OnUpdateValue?.Invoke(bytes[0], values);
        }
        catch (FormatException e)
        {
            Console.WriteLine(e);
        }
    }

    public byte[]? GetByteValues(byte row)
    {
        return _values.GetValueOrDefault(row);
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