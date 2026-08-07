namespace CPanelInterface.BadApple;

using System.Text.Json;
using RefMap = BidirectionalDictionary<ButtonRef, Vec2I>;

public class BtnMapping
{
    public RefMap Mapping { get; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public Vec2I GetPos(ButtonRef button)
    {
        return Mapping.GetValueOrDefault(button);
    }

    public ButtonRef GetButton(Vec2I pos)
    {
        return Mapping.Inverse.GetValueOrDefault(pos);
    }

    /// <summary>
    /// Write the mapping to disk as JSON, sorted by position for a stable diff.
    /// </summary>
    public void Save(string path)
    {
        var entries = Mapping
            .Select(kv => new BtnMappingEntry(kv.Key.Row, kv.Key.Idx, kv.Value.X, kv.Value.Y))
            .OrderBy(e => e.Y).ThenBy(e => e.X)
            .ToList();

        File.WriteAllText(path, JsonSerializer.Serialize(entries, JsonOptions));
        Console.WriteLine("Wrote to " + Path.GetFullPath(path));

    }

    /// <summary>
    /// Read a mapping written by <see cref="Save"/>. Returns null when the file doesn't exist, which
    /// is the caller's signal that the panel still needs to be walked.
    /// </summary>
    public static BtnMapping? Load(string path)
    {
        if (!File.Exists(path)) return null;

        using FileStream stream = File.OpenRead(path);
        return Load(stream, path);
    }

    /// <summary>
    /// Read a mapping from any JSON stream, such as an assembly resource. Does not dispose the stream.
    /// </summary>
    public static BtnMapping Load(Stream stream, string source = "mapping")
    {
        var entries = JsonSerializer.Deserialize<List<BtnMappingEntry>>(stream, JsonOptions)
                      ?? throw new InvalidDataException($"{source} is empty.");

        var mapping = new BtnMapping();
        foreach (BtnMappingEntry entry in entries)
        {
            ButtonRef button = ButtonRef.Of(entry.Row, entry.Idx);
            Vec2I pos = new Vec2I { X = entry.X, Y = entry.Y };
            try
            {
                mapping.Mapping[button] = pos;
            }
            catch (ArgumentException ex)
            {
                // The reverse index rejects duplicate values too, and says so without naming the source.
                throw new InvalidDataException(
                    $"{source}: {button} claims {pos}, which another button already holds.", ex);
            }
        }

        return mapping;
    }
}

public record BtnMappingEntry(byte Row, byte Idx, int X, int Y);

public class BtnMappingGen
{
    public static Task<RefMap> Gen(BtnMapping mapping, Panel panel)
    {
        return new BtnMappingGen(mapping, panel).Task;
    }
    
    private static readonly ButtonRef Take = ButtonRef.Of(65, 5);
    private static readonly ButtonRef Auto = ButtonRef.Of(65, 6);

    private readonly BtnMapping _mapping;
    private readonly TaskCompletionSource<RefMap> _tcs = new();
    private readonly Panel _panel;

    public BtnMappingGen(BtnMapping mapping, Panel panel)
    {
        _mapping = mapping;
        _panel = panel;

        panel.Parser.OnPressButton += OnButton;
    }

    private Vec2I _pos = default;
    
    public Task<RefMap> Task => _tcs.Task;

    private int _count;
    
    private void OnButton(ButtonRef button, bool pressed)
    {
        if (Task.IsCompleted) return;
        if (!pressed) return;
        if (button == Take)
        {
            _pos.X = 0;
            _pos.Y++;
            Console.WriteLine("New line");
        }
        else if (button == Auto)
        {
            Console.WriteLine($"Finished mapping {_count} buttons");
            _tcs.SetResult(_mapping.Mapping);
            _panel.Parser.OnPressButton -= OnButton;
        }
        else
        {
            _mapping.Mapping[button] = _pos;
            Console.WriteLine($"Assigned {button} to {_pos}");
            _pos.X++;
            _count++;
            _panel.Leds.SetLedState(button, true);
        }
    }
}

public record struct Vec2I
{
    public int X { get; set; }
    public int Y { get; set; }

    public static Vec2I of(int x, int y)
    {
        return new Vec2I { X = x, Y = y };
    }
}