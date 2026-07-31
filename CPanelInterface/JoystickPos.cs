namespace CPanelInterface;

public record struct JoystickPos
{
    public byte X;
    public byte Y;
    public byte Roll;

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

    public static JoystickPos Of(byte[] bytes)
    {
        // Control surface sends Y first
        JoystickPos pos = new();
        if (bytes.Length > 0) pos.Y = bytes[0];
        if (bytes.Length > 1) pos.X = bytes[1];
        if (bytes.Length > 2) pos.Roll = bytes[2];
        return pos;
    }
}