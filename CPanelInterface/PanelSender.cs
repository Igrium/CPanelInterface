using System.Collections.Concurrent;

namespace CPanelInterface;

/// <summary>
/// Encodes and sends messages to a panel
/// </summary>
public class PanelSender
{
    private readonly Dictionary<byte, byte> _state = new();

    public PanelSender(PanelTransport transport)
    {
        Transport = transport;
    }

    public PanelTransport Transport { get; }

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
    public bool IsLEDOn(byte row, byte idx)
    {
        return GetBitAsBool(GetRowState(row), idx);
    }

    /// <summary>
    /// Reset all LEDs to off
    /// </summary>
    public void Reset()
    {
        for (byte i = 0; i < 255; i++)
        {
            _sendLedFlags(i, 0xFF);
        }
        _state.Clear();
    }

    /// <summary>
    /// Set the entire state of a row using bit flags
    /// </summary>
    /// <param name="row">Row index</param>
    /// <param name="flags">Bit flags representing row state</param>
    public void SetRowState(byte row, byte flags)
    {
        _sendLedFlags(row, flags);
        _state[row] = flags;
    }

    /// <summary>
    /// Set the powered state of a given LED
    /// </summary>
    /// <param name="row">Row index</param>
    /// <param name="idx">LED index</param>
    /// <param name="state">The powered state to set</param>
    public void SetLedState(byte row, byte idx, bool state)
    {
        SetRowState(row, SetBitAsBool(GetRowState(row), idx, !state));
    }

    private void _sendLedFlags(byte row, byte flags)
    {
        Transport.PushMessage(Convert.ToHexString([row, flags]));
    }

    private static bool GetBitAsBool(byte value, int bitIndex)
    {
        return ((value >> bitIndex) & 1) == 1;
    }

    private static byte SetBitAsBool(byte value, int bitIndex, bool bitValue)
    {
        return bitValue
            ? (byte)(value | (1 << bitIndex))
            : (byte)(value & ~(1 << bitIndex));
    }
}