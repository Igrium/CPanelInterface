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
        
        string folder = _getFolder();
        if (!Directory.Exists(folder))
        {
            Console.WriteLine($"Folder {folder} does not exist.");
            return;
        }
        
        
        Panel panel = Panel.Open(ports[0]);
        panel.Listener.Start();

        var frames = FrameReader.EnumerateFolder(folder, out var count);
        var info = new VideoInfo
        {
            Frames = frames,
            Fps = 15,
            NumFrames = count
        };
        
        new Player(panel, mapping, info, .25f).Play();
    }

    public static void Exit()
    {
        _wantsExit = true;
    }

    static void Tick(Panel panel)
    {
        Thread.Sleep(10);
    }

    static string _getFolder()
    {
        Console.WriteLine("Please enter the folder with frames");
        return Console.ReadLine() ?? "";
    }
}

