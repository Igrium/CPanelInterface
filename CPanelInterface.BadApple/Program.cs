namespace CPanelInterface.BadApple;

using CPanelInterface;

public static class Program
{
    private static volatile bool _wantsExit;
    
    public static bool WantsExit => _wantsExit;

    public static void Main(string[] args)
    {
        // MappingProgram.GenMappings();
        MainPrimary(args);
    }

    static void MainPrimary(string[] args)
    {
        var ports = Panel.ChoosePorts();
        if (ports.Count == 0)
        {
            Console.Error.WriteLine("No control surface found.");
            return;
        }
        
        using Stream stream = typeof(BtnMapping).Assembly
            .GetManifestResourceStream("CPanelInterface.BadApple.btnMapping.json")!;

        BtnMapping mapping = BtnMapping.Load(stream, "btnMapping.json");
        Console.WriteLine("Loaded mappings");

        Panel panel = Panel.Open(ports[0]);
        panel.Listener.Start();

        
        while (!WantsExit && panel.Listener.IsOpen)
        {
            Tick(panel);
        }
    }

    public static void Exit()
    {
        _wantsExit = true;
    }

    static void Tick(Panel panel)
    {
        Thread.Sleep(10);
    }
}

