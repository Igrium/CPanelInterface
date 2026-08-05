using System.Collections;
using System.Text;
using CPanelInterface;

Console.WriteLine("Searching for control surfaces...");

IReadOnlyList<string> portNames = ChoosePorts();
if (portNames.Count == 0)
{
    Console.WriteLine("No control surface selected, exiting.");
    return;
}

var panels = new List<Panel>();
foreach (string name in portNames)
{
    try
    {
        panels.Add(Panel.Open(name, panels.Count));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
    {
        Console.WriteLine($"Could not open {name}: {ex.Message}");
    }
}

if (panels.Count == 0)
{
    Console.WriteLine("Nothing could be opened, exiting.");
    return;
}

Console.WriteLine($"Opened {panels.Count} panel(s). Listening for messages (Ctrl+C to exit)...");

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    foreach (Panel panel in panels) panel.Listener.Stop();
};

PrintMenu();

// Quit once every panel has stopped -- one being unplugged shouldn't take the others down.
while (panels.Any(p => p.Listener.IsOpen && p.Listener.Running))
{
    if (Console.KeyAvailable)
    {
        HandleKey(Console.ReadKey(intercept: true).KeyChar, panels);
    }
    else
    {
        Thread.Sleep(50);
    }
}

Console.WriteLine("Listener stopped.");
foreach (Panel panel in panels) panel.Dispose();

// Auto-detect surfaces, falling back to asking when nothing can be confirmed.
static IReadOnlyList<string> ChoosePorts()
{
    IReadOnlyList<PanelPortInfo> discovered = PanelDiscovery.DiscoverAll();

    var confirmed = discovered.Where(p => p.Confidence == PanelConfidence.Confirmed).ToList();
    if (confirmed.Count > 0)
    {
        foreach (PanelPortInfo panel in confirmed)
        {
            Console.WriteLine($"  Found {panel.Port} - {panel.Reason}");
        }

        return confirmed.Select(p => p.PortName).ToList();
    }

    // Nothing confirmed. Unconfirmed candidates are still worth offering, but not opening blindly.
    if (discovered.Count > 0)
    {
        Console.WriteLine("No surface confirmed. Possible candidates:");
        foreach (PanelPortInfo panel in discovered)
        {
            Console.WriteLine($"  {panel.Port} - {panel.Reason}");
        }
    }
    else
    {
        Console.WriteLine("No surfaces or likely candidates found.");
    }

    Console.Write("Enter serial port name (e.g. COM3 or /dev/cu.usbserial-XXXX), or blank to quit: ");
    string? manual = Console.ReadLine();
    return string.IsNullOrWhiteSpace(manual) ? [] : [manual.Trim()];
}

static void PrintMenu()
{
    Console.WriteLine();
    Console.WriteLine("Commands: [b] query button  [v] query raw/float value  [j] query joystick  [m] show this menu  [Ctrl+C] quit");
    Console.WriteLine();
}

static void HandleKey(char key, List<Panel> panels)
{
    char lowered = char.ToLowerInvariant(key);
    if (lowered == 'm')
    {
        PrintMenu();
        return;
    }

    if (lowered is not ('b' or 'v' or 'j')) return;

    Panel? panel = ChoosePanel(panels);
    if (panel == null) return;

    switch (lowered)
    {
        case 'b':
            QueryButton(panel.Parser);
            break;
        case 'v':
            QueryValue(panel.Parser);
            break;
        case 'j':
            QueryJoystick(panel.Parser);
            break;
    }
}

// Ask which panel a query applies to, skipping the prompt when there's only one.
static Panel? ChoosePanel(List<Panel> panels)
{
    if (panels.Count == 1) return panels[0];

    foreach (Panel panel in panels)
    {
        Console.WriteLine($"  [{panel.Index}] {panel.PortName}");
    }

    Console.Write("Panel: ");
    if (int.TryParse(Console.ReadLine(), out int index) && index >= 0 && index < panels.Count)
    {
        return panels[index];
    }

    Console.WriteLine("Invalid panel.");
    return null;
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

/// <summary>
/// One open control surface and everything hanging off it. Output is prefixed with the panel index
/// so several attached at once stay distinguishable.
/// </summary>
sealed class Panel : IDisposable
{
    public required int Index { get; init; }
    public required string PortName { get; init; }
    public required PanelTransport Transport { get; init; }
    public required PanelTransport.Listener Listener { get; init; }
    public required PanelParser Parser { get; init; }
    public required LedManager Leds { get; init; }

    public static Panel Open(string portName, int index)
    {
        var transport = new PanelTransport(portName);
        transport.Open();

        var listener = new PanelTransport.Listener(transport);
        var leds = new LedManager(transport);

        var panel = new Panel
        {
            Index = index,
            PortName = portName,
            Transport = transport,
            Listener = listener,
            Parser = new PanelParser(listener),
            Leds = leds
        };

        leds.Reset();

        string tag = $"[{index}]";

        listener.OnError += ex => Console.WriteLine($"{tag} Error: {ex.Message}");

        panel.Parser.OnUpdateJoystick += (row, joystick) =>
            Console.WriteLine($"{tag} Joystick {row}: ({joystick.X}, {joystick.Y}, {joystick.Roll})");

        panel.Parser.OnUpdateEncoder += (row, value) =>
            Console.WriteLine($"{tag} Encoder {row}: {value}");

        panel.Parser.OnPressButton += (button, pressed) =>
        {
            Console.WriteLine($"{tag} Row {button.Row}, button {button.Idx}: {(pressed ? "pressed" : "released")}");
            leds.SetLedState(button, pressed);
        };

        listener.Start();
        return panel;
    }

    public void Dispose()
    {
        Listener.Dispose();
        Transport.Dispose();
    }
}
