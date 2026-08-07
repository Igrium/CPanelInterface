namespace CPanelInterface;

public record struct JoystickPos
{
    public byte X { get; set; }
    public byte Y { get; set; }
    public byte Roll { get; set; }

    public float XPercent => X / 255f;
    public float YPercent => Y / 255f;
    public float RollPercent => Roll / 255f;

    public static JoystickPos Of(byte x, byte y, byte roll)
    {
        return new JoystickPos()
        {
            X = x,
            Y = y,
            Roll = roll
        };
    }

    public static JoystickPos Of(IList<byte> bytes)
    {
        // Control surface sends Y first
        JoystickPos pos = new();
        if (bytes.Count > 0) pos.Y = bytes[0];
        if (bytes.Count > 1) pos.X = bytes[1];
        if (bytes.Count > 2) pos.Roll = bytes[2];
        return pos;
    }
}