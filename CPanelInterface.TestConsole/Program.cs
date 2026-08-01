using System.Collections;
using System.Text;
using CPanelInterface;

Console.Write("Enter serial port name (e.g. COM3 or /dev/tty.usbserial-XXXX): ");
string? portName = Console.ReadLine();
if (string.IsNullOrWhiteSpace(portName))
{
    Console.WriteLine("No port name given, exiting.");
    return;
}

using var transport = new PanelTransport(portName);
transport.Open();
Console.WriteLine($"Opened {portName}. Listening for messages (Ctrl+C to exit)...");

using var listener = new PanelTransport.Listener(transport);

listener.OnError += ex => Console.WriteLine($"Error: {ex.Message}");

var parser = new PanelParser(listener);
var sender = new LedManager(transport);

sender.Reset();

parser.OnUpdateJoystick += (row, joystick) =>
{
    Console.WriteLine($"Joystick {row}: ({joystick.X}, {joystick.Y}, {joystick.Roll})");
};

parser.OnUpdateEncoder += (row, value) =>
{
    Console.WriteLine($"Encoder {row}: {value}");
};

parser.OnPressButton += (button, pressed) =>
{
    Console.WriteLine($"Row {button.Row}, button {button.Idx}: {(pressed ? "pressed" : "released")}");
    sender.SetLedState(button, pressed);
};

listener.Start();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    listener.Stop();
};

PrintMenu();

while (listener.IsOpen && listener.Running)
{
    if (Console.KeyAvailable)
    {
        HandleKey(Console.ReadKey(intercept: true).KeyChar, parser);
    }
    else
    {
        Thread.Sleep(50);
    }
}

Console.WriteLine("Listener stopped.");

static void PrintMenu()
{
    Console.WriteLine();
    Console.WriteLine("Commands: [b] query button  [v] query raw/float value  [j] query joystick  [m] show this menu  [Ctrl+C] quit");
    Console.WriteLine();
}

static void HandleKey(char key, PanelParser parser)
{
    switch (char.ToLowerInvariant(key))
    {
        case 'b':
            QueryButton(parser);
            break;
        case 'v':
            QueryValue(parser);
            break;
        case 'j':
            QueryJoystick(parser);
            break;
        case 'm':
            PrintMenu();
            break;
    }
}

static bool TryReadRow(string prompt, out byte row)
{
    Console.Write(prompt);
    string? input = Console.ReadLine();
    return byte.TryParse(input, out row);
}

static void QueryButton(PanelParser parser)
{
    if (!TryReadRow("Row: ", out byte row))
    {
        Console.WriteLine("Invalid row.");
        return;
    }

    Console.Write("Button index: ");
    if (!int.TryParse(Console.ReadLine(), out int button))
    {
        Console.WriteLine("Invalid button index.");
        return;
    }

    bool state = parser.GetButtonState(row, button);
    Console.WriteLine($"Row {row}, button {button}: {(state ? "pressed" : "released")}");
}

static void QueryValue(PanelParser parser)
{
    if (!TryReadRow("Row: ", out byte row))
    {
        Console.WriteLine("Invalid row.");
        return;
    }

    bool known = parser.GetByteValue(row, out byte value);
    float percent = parser.GetFloatValue(row);
    Console.WriteLine($"Row {row}: byte={value} float={percent:F3}{(known ? "" : " (no data received yet, defaulted)")}");
}

static void QueryJoystick(PanelParser parser)
{
    if (!TryReadRow("Row: ", out byte row))
    {
        Console.WriteLine("Invalid row.");
        return;
    }

    if (!parser.GetJoysickValue(row, out JoystickPos pos))
    {
        Console.WriteLine($"Row {row}: no data received yet.");
        return;
    }

    Console.WriteLine($"Row {row}: X={pos.X} ({pos.XPercent:F3}), Y={pos.Y} ({pos.YPercent:F3}), Roll={pos.Roll} ({pos.RollPercent:F3})");
}

// Source - https://stackoverflow.com/a/8991834
// Posted by oleksii
// Retrieved 2026-07-31, License - CC BY-SA 3.0

static string ToBitString(BitArray bits)
{
    var sb = new StringBuilder();

    for (int i = 0; i < bits.Count; i++)
    {
        char c = bits[i] ? '1' : '0';
        sb.Append(c);
    }

    return sb.ToString();
}
