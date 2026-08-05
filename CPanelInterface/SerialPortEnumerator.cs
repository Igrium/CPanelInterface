using System.Diagnostics;
using System.IO.Ports;
using System.Runtime.Versioning;
using System.Security;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CPanelInterface;

/// <summary>
/// Lists serial ports along with the USB metadata behind them.
///
/// <see cref="SerialPort.GetPortNames"/> returns bare strings with no manufacturer, product, VID/PID
/// or serial number, and there is no cross-platform API that adds them — so each OS is handled
/// separately here: IOKit on macOS, sysfs on Linux, the registry on Windows. Every path degrades to
/// plain port names rather than throwing, so <see cref="PanelDiscovery"/>'s protocol probe always has
/// something to work with.
/// </summary>
public static class SerialPortEnumerator
{
    /// <summary>
    /// Every serial port on the machine, with metadata where the OS provides it.
    /// </summary>
    public static IReadOnlyList<SerialPortInfo> ListPorts()
    {
        try
        {
            if (OperatingSystem.IsMacOS()) return ListMacOs();
            if (OperatingSystem.IsLinux()) return ListLinux();
            if (OperatingSystem.IsWindows()) return ListWindows();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Fall through to the bare list below: no metadata is still usable.
        }

        return ListBare();
    }

    /// <summary>
    /// Last-resort listing with no metadata at all.
    /// </summary>
    private static List<SerialPortInfo> ListBare() =>
        SerialPort.GetPortNames().Select(name => new SerialPortInfo(name)).ToList();

    // ---------------------------------------------------------------- macOS

    /// <summary>
    /// Matches the header line of an ioreg entry, e.g. <c>+-o NewTek Control Surface@08300000  &lt;class ...</c>
    /// </summary>
    private static readonly Regex IoRegEntry = new(@"^\s*\+-o\s+(.+?)@", RegexOptions.Compiled);

    /// <summary>
    /// Matches an ioreg property line, e.g. <c>"USB Serial Number" = "FTYUVF8W"</c>
    /// </summary>
    private static readonly Regex IoRegProperty = new("^\\s*\"([^\"]+)\"\\s*=\\s*(.+?)\\s*$", RegexOptions.Compiled);

    [SupportedOSPlatform("macos")]
    private static List<SerialPortInfo> ListMacOs()
    {
        // Only the callout devices. Opening the matching /dev/tty.* blocks until carrier detect,
        // which an FTDI bridge never asserts, so those are never useful to us.
        List<string> ports = Directory.Exists("/dev")
            ? Directory.EnumerateFileSystemEntries("/dev", "cu.*").Order(StringComparer.Ordinal).ToList()
            : [];

        // macOS names FTDI ports after the USB serial number ("cu.usbserial-FTYUVF8W"), which is the
        // join key back to the descriptor strings from IOKit.
        var bySerial = ReadIoRegUsbDevices();

        var result = new List<SerialPortInfo>(ports.Count);
        foreach (string port in ports)
        {
            string name = Path.GetFileName(port);
            SerialPortInfo? match = bySerial
                .Where(device => device.SerialNumber != null && name.EndsWith(device.SerialNumber, StringComparison.Ordinal))
                .Select(device => device with { PortName = port })
                .FirstOrDefault();

            result.Add(match ?? new SerialPortInfo(port));
        }

        return result;
    }

    /// <summary>
    /// Every USB device IOKit knows about, with its descriptor strings. Returns an empty list if
    /// ioreg is missing or misbehaves — callers must treat metadata as optional.
    /// </summary>
    [SupportedOSPlatform("macos")]
    private static List<SerialPortInfo> ReadIoRegUsbDevices()
    {
        string output;
        try
        {
            using var process = Process.Start(new ProcessStartInfo("/usr/sbin/ioreg")
            {
                // -d 1 keeps the output to one line-oriented block per device rather than the whole
                // IOKit tree, which is far cheaper to run and to parse.
                ArgumentList = { "-r", "-c", "IOUSBHostDevice", "-l", "-d", "1" },
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process == null) return [];

            output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(5000)) return [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return [];
        }

        var devices = new List<SerialPortInfo>();
        var current = new Dictionary<string, string>(StringComparer.Ordinal);

        void Flush()
        {
            if (current.Count == 0) return;

            devices.Add(new SerialPortInfo(
                PortName: "", // Filled in by the caller once a tty is matched to it.
                Manufacturer: current.GetValueOrDefault("USB Vendor Name")?.Trim(),
                Product: current.GetValueOrDefault("USB Product Name")?.Trim(),
                SerialNumber: current.GetValueOrDefault("USB Serial Number")?.Trim(),
                VendorId: ParseDecimalId(current.GetValueOrDefault("idVendor")),
                ProductId: ParseDecimalId(current.GetValueOrDefault("idProduct"))));

            current.Clear();
        }

        foreach (string line in output.Split('\n'))
        {
            if (IoRegEntry.IsMatch(line))
            {
                Flush();
                continue;
            }

            Match property = IoRegProperty.Match(line);
            if (property.Success)
            {
                current[property.Groups[1].Value] = property.Groups[2].Value.Trim('"');
            }
        }

        Flush();
        return devices;
    }

    /// <summary>
    /// ioreg prints numeric IDs in decimal, so 0x0403 arrives as "1027".
    /// </summary>
    private static ushort? ParseDecimalId(string? value) =>
        ushort.TryParse(value, out ushort parsed) ? parsed : null;

    // ---------------------------------------------------------------- Linux

    [SupportedOSPlatform("linux")]
    private static List<SerialPortInfo> ListLinux()
    {
        var result = new List<SerialPortInfo>();

        foreach (string port in SerialPort.GetPortNames().Order(StringComparer.Ordinal))
        {
            result.Add(ReadLinuxSysfs(port) ?? new SerialPortInfo(port));
        }

        return result;
    }

    /// <summary>
    /// Read USB descriptor fields for a tty out of sysfs. The idVendor/idProduct/serial files live on
    /// the USB device node, several levels above the tty's own device link, so walk up looking for them.
    /// </summary>
    [SupportedOSPlatform("linux")]
    private static SerialPortInfo? ReadLinuxSysfs(string port)
    {
        string dir = $"/sys/class/tty/{Path.GetFileName(port)}/device";
        if (!Directory.Exists(dir)) return null;

        for (int depth = 0; depth < 5; depth++)
        {
            if (File.Exists(Path.Combine(dir, "idVendor")))
            {
                return new SerialPortInfo(
                    port,
                    Manufacturer: ReadSysfsString(dir, "manufacturer"),
                    Product: ReadSysfsString(dir, "product"),
                    SerialNumber: ReadSysfsString(dir, "serial"),
                    VendorId: ParseHexId(ReadSysfsString(dir, "idVendor")),
                    ProductId: ParseHexId(ReadSysfsString(dir, "idProduct")));
            }

            dir = Path.Combine(dir, "..");
        }

        return null;
    }

    private static string? ReadSysfsString(string directory, string file)
    {
        try
        {
            string path = Path.Combine(directory, file);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// sysfs and the Windows registry both spell IDs as bare hex, e.g. "0403".
    /// </summary>
    private static ushort? ParseHexId(string? value) =>
        ushort.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out ushort parsed)
            ? parsed
            : null;

    // -------------------------------------------------------------- Windows

    /// <summary>
    /// Matches an FTDIBUS enumeration key, e.g. <c>VID_0403+PID_6001+FTYUVF8WA</c>. FTDI appends a
    /// port letter to the serial number, hence the trailing character in the last group.
    /// </summary>
    private static readonly Regex FtdiBusKey =
        new(@"^VID_([0-9A-F]{4})\+PID_([0-9A-F]{4})\+(.+?)A?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [SupportedOSPlatform("windows")]
    private static List<SerialPortInfo> ListWindows()
    {
        // The FTDI VCP driver records its COM assignment in the enumeration key, so the port name,
        // VID/PID and serial number all come from one registry walk -- no WMI dependency needed.
        var byPort = new Dictionary<string, SerialPortInfo>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using RegistryKey? ftdiBus = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\FTDIBUS");
            if (ftdiBus != null)
            {
                foreach (string deviceKeyName in ftdiBus.GetSubKeyNames())
                {
                    Match parsed = FtdiBusKey.Match(deviceKeyName);
                    if (!parsed.Success) continue;

                    using RegistryKey? deviceKey = ftdiBus.OpenSubKey(deviceKeyName);
                    if (deviceKey == null) continue;

                    foreach (string instanceName in deviceKey.GetSubKeyNames())
                    {
                        using RegistryKey? parameters = deviceKey.OpenSubKey($@"{instanceName}\Device Parameters");
                        if (parameters?.GetValue("PortName") is not string portName) continue;

                        using RegistryKey? instance = deviceKey.OpenSubKey(instanceName);

                        byPort[portName] = new SerialPortInfo(
                            portName,
                            // Mfg is an INF-relative string like "%ftdi%;FTDI"; take the resolved half.
                            Manufacturer: (instance?.GetValue("Mfg") as string)?.Split(';').Last(),
                            // The bus-reported description is the EEPROM product string; FriendlyName
                            // is the INF's generic "USB Serial Port (COM3)", so prefer the former.
                            Product: instance?.GetValue("BusReportedDeviceDesc") as string
                                     ?? instance?.GetValue("FriendlyName") as string,
                            SerialNumber: parsed.Groups[3].Value,
                            VendorId: ParseHexId(parsed.Groups[1].Value),
                            ProductId: ParseHexId(parsed.Groups[2].Value));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Registry unreadable; fall back to bare names below.
        }

        return SerialPort.GetPortNames()
            .Order(StringComparer.Ordinal)
            .Select(name => byPort.GetValueOrDefault(name) ?? new SerialPortInfo(name))
            .ToList();
    }
}
