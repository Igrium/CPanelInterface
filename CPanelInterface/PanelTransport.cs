using System.IO.Ports;
using System.Text;

namespace CPanelInterface;

public class PanelTransport : IDisposable
{
    private readonly SerialPort _port;
    
    public bool IsOpen => _port.IsOpen;
    
    public PanelTransport(string portName, int baudRate = 9600,
        Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One)
    {
        _port = new SerialPort(portName, baudRate, parity, dataBits, stopBits);
        _port.ReadTimeout = 500_000;
        _port.WriteTimeout = 500;
    }

    public void Open()
    {
        _port.Open();
    }

    /// <summary>
    /// Block until a new message arrives from the device
    /// </summary>
    /// <returns>The message encoded as ascii (excluding \r)</returns>
    public string PollMessage()
    {
        StringBuilder sb = new StringBuilder(64);
        while (true)
        {
            int b = _port.ReadByte();
            if (b == '\r') return sb.ToString();
            sb.Append((char)b);
        }
    }

    public void Dispose()
    {
        _port.Close();
    }
    
    /// <summary>
    /// A way to query the panel via callbacks instead of blocking
    /// </summary>
    /// <param name="transport">Panel transport to use. It is recommended not to touch this after starting.</param>
    public class Listener(PanelTransport transport) : IDisposable
    {

        public bool IsOpen => transport.IsOpen;

        public delegate void MessageHandler(string message);
        public delegate void ErrorHandler(Exception ex);

        public event MessageHandler? OnMessage;
        public event ErrorHandler? OnError;

        private Thread? _thread;
        
        public bool Running { get; private set; }
        
        public void Start()
        {
            if (Running || _thread != null) throw new InvalidOperationException("Already started");
            Running = true;
            _thread = new Thread(RunThread) { IsBackground = true };
            _thread.Start();
        }

        private void RunThread()
        {
            try
            {
                while (Running && transport.IsOpen)
                {
                    OnMessage?.Invoke(transport.PollMessage());
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
                Console.WriteLine(ex);
            }
            finally
            {
                if (transport.IsOpen)
                {
                    transport.Dispose();
                }
            }
        }

        /**
         * Dispose of this transport and return immediately.
         */
        public void Stop()
        {
            Running = false;
        }
        
        /// <summary>
        /// Dispose of this transport and block until it's complete
        /// </summary>
        public void Dispose()
        {
            Running = false;
            if (_thread != null && Thread.CurrentThread != _thread)
            {
                _thread.Join();
            }
        }
    }
}