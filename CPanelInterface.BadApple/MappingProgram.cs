namespace CPanelInterface.BadApple;

public static class MappingProgram
{
    public static async Task GenMappingsAsync()
    {
        var ports = Panel.ChoosePorts();
        if (ports.Count == 0)
        {
            Console.Error.WriteLine("No control surface found.");
            return;
        }

        Panel panel = Panel.Open(ports[0]);
        panel.Listener.Start();
        
        BtnMapping mapping = new BtnMapping();
        await BtnMappingGen.Gen(mapping, panel);
        mapping.Save("btnMapping.json");
        
    }

    public static void GenMappings()
    {
        GenMappingsAsync().Wait();
    }
}