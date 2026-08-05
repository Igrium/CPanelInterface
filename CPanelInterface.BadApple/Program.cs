namespace CPanelInterface.BadApple;

using CPanelInterface;

public static class Program
{
    public static void Main(string[] args)
    {
        var ports = ChoosePorts();
        if (ports.Count == 0)
        {
            Console.Error.WriteLine("No control surface found.");
            return;
        }

        Panel panel = Panel.Open(ports[0]);
    }
    
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
}

