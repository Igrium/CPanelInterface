using System.Collections.Concurrent;
using System.IO.Ports;

namespace CPanelInterface;

/// <summary>
/// Finds attached control surfaces so callers never have to name a port.
///
/// Two layers: <see cref="SerialPortEnumerator"/> supplies USB metadata, which on its own identifies
/// the surface outright when the descriptor strings come through; anything that only looks plausible
/// is then confirmed by <see cref="Probe"/>, which listens for the protocol itself and so works
/// identically on every OS.
/// </summary>
public static class PanelDiscovery
{
    /// <summary>
    /// The surface's USB descriptor is vendor-written, so its strings name the manufacturer outright.
    /// Matched as a case-insensitive substring: descriptor strings routinely carry padding, and a
    /// larger surface may well change its product string while keeping the manufacturer.
    /// </summary>
    private const string VendorName = "NewTek";

    /// <summary>
    /// FTDI's USB vendor ID. Narrowing only — it's shared by thousands of unrelated products.
    /// </summary>
    private const ushort FtdiVendorId = 0x0403;

    /// <summary>
    /// Every attached port that is or might be a control surface, most likely first.
    ///
    /// Only <see cref="PanelConfidence.Confirmed"/> entries should be opened without asking;
    /// <see cref="PanelConfidence.Candidate"/> entries are worth offering as a manual choice.
    /// </summary>
    /// <param name="probe">
    /// Whether to confirm candidates with <see cref="Probe"/>. Probing opens and writes to a port, so
    /// only candidates are ever probed — never every port on the machine.
    /// </param>
    /// <param name="probeTimeoutMs">How long to wait for each candidate to reply.</param>
    public static IReadOnlyList<PanelPortInfo> DiscoverAll(bool probe = true, int probeTimeoutMs = 1000)
    {
        List<PanelPortInfo> found = SerialPortEnumerator.ListPorts()
            .Select(Classify)
            .Where(result => result.Confidence != PanelConfidence.No)
            .ToList();

        if (probe)
        {
            var promoted = new ConcurrentDictionary<string, PanelPortInfo>(StringComparer.Ordinal);

            // Probed in parallel: the timeout dominates, and one unresponsive adapter shouldn't hold
            // up the rest.
            Parallel.ForEach(found.Where(f => f.Confidence == PanelConfidence.Candidate), candidate =>
            {
                if (Probe(candidate.PortName, probeTimeoutMs))
                {
                    promoted[candidate.PortName] = candidate with
                    {
                        Confidence = PanelConfidence.Confirmed,
                        Reason = "speaks the panel protocol"
                    };
                }
            });

            found = found.Select(f => promoted.GetValueOrDefault(f.PortName) ?? f).ToList();
        }

        return found
            .OrderByDescending(f => f.Confidence)
            .ThenBy(f => f.PortName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The confirmed surfaces only — what an application should open on startup.
    /// </summary>
    public static IReadOnlyList<PanelPortInfo> DiscoverConfirmed(int probeTimeoutMs = 1000) =>
        DiscoverAll(probe: true, probeTimeoutMs)
            .Where(p => p.Confidence == PanelConfidence.Confirmed)
            .ToList();

    /// <summary>
    /// Decide what a port looks like from its USB metadata alone. Cheap, and never touches the port.
    /// </summary>
    public static PanelPortInfo Classify(SerialPortInfo port)
    {
        if (Contains(port.Product, VendorName) || Contains(port.Manufacturer, VendorName))
        {
            return new PanelPortInfo(port, PanelConfidence.Confirmed, $"USB descriptor names {VendorName}");
        }

        if (port.VendorId == FtdiVendorId)
        {
            return new PanelPortInfo(port, PanelConfidence.Candidate, "FTDI USB-serial bridge");
        }

        // macOS gives us no VID unless ioreg came through, but "cu.usbserial-" is specifically what
        // Apple's FTDI driver emits -- CDC-ACM devices get "usbmodem" instead. Advisory only.
        if (OperatingSystem.IsMacOS() && Path.GetFileName(port.PortName).StartsWith("cu.usbserial", StringComparison.Ordinal))
        {
            return new PanelPortInfo(port, PanelConfidence.Candidate, "FTDI-style device name");
        }

        return new PanelPortInfo(port, PanelConfidence.No, "no USB metadata suggesting a control surface");
    }

    /// <summary>
    /// Ask a port to identify itself, and report whether it answers like a control surface.
    ///
    /// The surface is completely silent until told to talk — measured, and it settles open question 7
    /// in <c>PROTOCOL.md</c>: not one byte arrives before a command, so a listen-only probe can never
    /// confirm anything. <c>I\r</c> is the right thing to send: it draws an immediate <c>~009</c> and,
    /// unlike <c>T\r</c>, does not start the telemetry stream, so the port is left as it was found.
    ///
    /// Because this writes, only run it on ports that already look plausible (see
    /// <see cref="Classify"/>) — opening a port asserts DTR/RTS, which resets some microcontroller
    /// boards, and a stray <c>I</c> shouldn't land on an unrelated device.
    /// </summary>
    /// <param name="portName">Port to probe. Must not already be open.</param>
    /// <param name="timeoutMs">How long to wait for a reply before giving up.</param>
    public static bool Probe(string portName, int timeoutMs = 1000)
    {
        try
        {
            using var port = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One)
            {
                NewLine = "\r",
                // Read in slices so a silent port doesn't burn the whole budget in one call.
                ReadTimeout = Math.Min(timeoutMs, 250),
                WriteTimeout = 500
            };

            port.Open();
            port.DiscardInBuffer();
            port.WriteLine("I");

            long deadline = Environment.TickCount64 + timeoutMs;
            int frames = 0;

            while (Environment.TickCount64 < deadline)
            {
                string line;
                try
                {
                    line = port.ReadLine();
                }
                catch (TimeoutException)
                {
                    continue;
                }

                // The heartbeat is an empty message, and only runs once telemetry has been started.
                // Consistent with a panel, but a bare \r is weak evidence on its own.
                if (line.Length == 0) continue;

                // "~XXX" is the handshake reply we just asked for. Conclusive.
                if (line.StartsWith('~')) return true;

                // If something already started telemetry on this port, we'll see the "AAVV"
                // address+value form instead. One could be a coincidence; three could not.
                if (line.Length >= 4 && line.Length % 2 == 0 && line.All(Uri.IsHexDigit) && ++frames >= 3)
                {
                    return true;
                }
            }

            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false; // Already open, by us or by someone else.
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            return false; // Vanished mid-probe, isn't a real serial device, or a malformed name.
        }
    }

    private static bool Contains(string? haystack, string needle) =>
        haystack != null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
