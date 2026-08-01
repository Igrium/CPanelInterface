using System.Collections.Concurrent;

namespace CPanelInterface;

/// <summary>
/// Encodes and sends messages to a panel
/// </summary>
public class LedManager
{
    private readonly ConcurrentDictionary<byte, byte> _state = new();

    public LedManager(PanelTransport transport)
    {
        Transport = transport;
    }

    public PanelTransport Transport { get; }
    
    // So writes don't override each-other (reads are atomic)
    private readonly Lock _lock = new Lock();

    /// <summary>
    /// Get the cached LED state of a given row
    /// </summary>
    /// <param name="row">Row in question</param>
    /// <returns>Bitflags of LED state</returns>
    public byte GetRowState(byte row)
    {
        return _state.GetValueOrDefault(row, (byte)0xFF);
    }

    /// <summary>
    /// Check if a given LED is on (cached)
    /// </summary>
    /// <param name="row">Row to target</param>
    /// <param name="idx">Index within row</param>
    /// <returns>If the LED is cached as on</returns>
    public bool IsLedOn(byte row, byte idx)
    {   
        return _bitAsBool(GetRowState(row), idx);
    }

    public bool IsLedOn(ButtonRef button)
    {
        return IsLedOn(button.Row, button.Idx);
    }

    /// <summary>
    /// Reset all LEDs to off
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _state.Clear();

            for (int i = 0; i <= 255; i++)
            {
                _sendLedFlags((byte)i, 0xFF);
            }
        }
    }

    /// <summary>
    /// Set the entire state of a row using bit flags
    /// </summary>
    /// <param name="row">Row index</param>
    /// <param name="flags">Bit flags representing row state</param>
    public void SetRowState(byte row, byte flags)
    {
        lock (_lock)
        {
            _sendLedFlags(row, flags);
            _state[row] = flags;
        }
    }

    public void SetRowState(byte row, ReadOnlySpan<bool> buttons)
    {
        SetRowState(row, _bitsToBytes(buttons));       
    }

    /// <summary>
    /// Set the state of a bunch of buttons at once
    /// </summary>
    /// <param name="states">The button states to send</param>
    public void SetAllStates(IDictionary<ButtonRef, bool> states)
    {
        if (states.Count == 0) return;

        // Row is a byte, so the entire row space fits on the stack (512 bytes).                                                                                                                                                                                                                                     
        Span<byte> rowValues = stackalloc byte[256];
        Span<bool> touched = stackalloc bool[256];

        lock (_lock)
        {
            foreach (var (button, state) in states)
            {
                int row = button.Row;
                if (!touched[row])
                {
                    rowValues[row] = _state.GetValueOrDefault(button.Row, (byte)0xFF);
                    touched[row] = true;
                }

                rowValues[row] = _setBitAsBool(rowValues[row], button.Idx, !state);
            }

            for (int row = 0; row < 256; row++)
            {
                if (touched[row])
                    SetRowState((byte)row, rowValues[row]);
            }
        }
        
    } 

    /// <summary>
    /// Set the powered state of a given LED
    /// </summary>
    /// <param name="row">Row index</param>
    /// <param name="idx">LED index</param>
    /// <param name="state">The powered state to set</param>
    public void SetLedState(byte row, byte idx, bool state)
    {
        lock (_lock)
        {
            SetRowState(row, _setBitAsBool(GetRowState(row), idx, !state));
        }
    }

    public void SetLedState(ButtonRef button, bool state)
    {
        SetLedState(button.Row, button.Idx, state);
    }

    private void _sendLedFlags(byte row, byte flags)
    {
        Transport.PushMessage(Convert.ToHexString([row, flags]));
    }

    private static bool _bitAsBool(byte value, int bitIndex)
    {
        return ((value >> bitIndex) & 1) == 1;
    }

    private static byte _setBitAsBool(byte value, int bitIndex, bool bitValue)
    {
        return bitValue
            ? (byte)(value | (1 << bitIndex))
            : (byte)(value & ~(1 << bitIndex));
    }

    private static byte _bitsToBytes(ReadOnlySpan<bool> bits, byte b = 0)
    {
        int len = Math.Max(bits.Length, 8);
        for (int i = 0; i < len; i++)
        {
            // Invert
            b = _setBitAsBool(b, i, !bits[i]);
        }

        return b;
    }
}