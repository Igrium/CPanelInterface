namespace CPanelInterface.BadApple;

/// <summary>
/// One open control surface and everything hanging off it. Output is prefixed with the panel index
/// so several attached at once stay distinguishable.
/// </summary>
public sealed class Panel : IDisposable
{
    public required string PortName { get; init; }
    public required PanelTransport Transport { get; init; }
    public required PanelTransport.Listener Listener { get; init; }
    public required PanelParser Parser { get; init; }
    public required LedManager Leds { get; init; }

    public static Panel Open(string portName)
    {
        var transport = new PanelTransport(portName);
        transport.Open();

        var listener = new PanelTransport.Listener(transport);
        
        var leds = new LedManager(transport);

        var panel = new Panel
        {
            PortName = portName,
            Transport = transport,
            Listener = listener,
            Parser = new PanelParser(listener),
            Leds = leds
        };

        leds.Reset();
        return panel;
    }

    public void Dispose()
    {
        Listener.Dispose();
        Transport.Dispose();
    }

    public Task WaitForButton(ButtonRef btn)
    {
        var tcs = new TaskCompletionSource();
        Parser.OnPressButton += (b, _) =>
        {
            if (b == btn)
            {
                tcs.TrySetResult();
            }
        };
        
        return tcs.Task;
    }

    public static IReadOnlyList<string> ChoosePorts()
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
}