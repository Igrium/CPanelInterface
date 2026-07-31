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
// listener.OnMessage += message => Console.WriteLine($"< {message}");
listener.OnError += ex => Console.WriteLine($"Error: {ex.Message}");
listener.Start();

listener.OnMessage += msg =>
{
    var bytes = Convert.FromHexString(msg);
    Console.WriteLine($"{bytes[0]}: {bytes[1]}");
};

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    listener.Stop();
};


while (listener.IsOpen && listener.Running)
{
    Thread.Sleep(200);
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



Console.WriteLine("Listener stopped.");