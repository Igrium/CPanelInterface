namespace CPanelInterface.BadApple;

/// <summary>
/// One open control surface and everything hanging off it. Output is prefixed with the panel index
/// so several attached at once stay distinguishable.
/// </summary>
sealed class Panel : IDisposable
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
}