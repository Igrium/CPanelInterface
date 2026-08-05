namespace CPanelInterface;

/// <summary>
/// A serial port together with whatever the OS knows about the USB device behind it.
/// </summary>
/// <param name="PortName">Name to hand to <see cref="PanelTransport"/>.</param>
/// <param name="Manufacturer">USB manufacturer string, e.g. <c>NewTek</c>. Null if unavailable.</param>
/// <param name="Product">USB product string, e.g. <c>NewTek Control Surface</c>. Null if unavailable.</param>
/// <param name="SerialNumber">
/// USB serial number, e.g. <c>FTYUVF8W</c>. This is the only identifier that stays stable across
/// replugs, so it's the key to use when several surfaces are attached at once.
/// </param>
/// <param name="VendorId">USB idVendor, e.g. <c>0x0403</c> for FTDI.</param>
/// <param name="ProductId">USB idProduct, e.g. <c>0x6001</c> for the FT232.</param>
public record SerialPortInfo(
    string PortName,
    string? Manufacturer = null,
    string? Product = null,
    string? SerialNumber = null,
    ushort? VendorId = null,
    ushort? ProductId = null)
{
    public override string ToString()
    {
        string? label = Product ?? Manufacturer;
        if (label == null) return PortName;
        return SerialNumber == null
            ? $"{PortName} ({label})"
            : $"{PortName} ({label}, {SerialNumber})";
    }
}

/// <summary>
/// How sure we are that a port is a control surface.
/// </summary>
public enum PanelConfidence
{
    /// <summary>
    /// Not a control surface, or nothing suggests it is. Never opened automatically.
    /// </summary>
    No,

    /// <summary>
    /// Plausible — an FTDI bridge, or a name matching the pattern Apple's FTDI driver emits — but
    /// nothing has confirmed it. Offer these as a manual choice rather than opening them blindly.
    /// </summary>
    Candidate,

    /// <summary>
    /// Confirmed, either by the USB descriptor naming NewTek or by the port speaking the panel
    /// protocol. Safe to open without asking.
    /// </summary>
    Confirmed
}

/// <summary>
/// A port that discovery considers interesting, and why.
/// </summary>
/// <param name="Port">The port and its USB metadata.</param>
/// <param name="Confidence">How sure we are.</param>
/// <param name="Reason">Human-readable explanation, for logging and for prompting the user.</param>
public record PanelPortInfo(SerialPortInfo Port, PanelConfidence Confidence, string Reason)
{
    public string PortName => Port.PortName;

    /// <summary>
    /// Stable identity across replugs, where the OS gives us one. Falls back to the port name,
    /// which is not stable but is better than nothing.
    /// </summary>
    public string Key => Port.SerialNumber ?? Port.PortName;

    public override string ToString() => $"{Port} [{Confidence}: {Reason}]";
}
