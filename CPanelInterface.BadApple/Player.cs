using System.Diagnostics;
using SkiaSharp;

namespace CPanelInterface.BadApple;

public class Player( Panel panel, BtnMapping mapping, VideoInfo video, float speed = 1)
{
    public Panel Panel => panel;
    public PanelParser Parser => panel.Parser;
    public BtnMapping Mapping => mapping;
    public VideoInfo Video => video;

    public float Speed => speed;
    
    /// <summary>
    /// The most-recently pushed frame
    /// </summary>
    public int Frame { get; private set; } = -1;

    public void Play()
    {
        double frameRate = Video.Fps * (double)Speed;
        int lastFrame = Video.NumFrames - 1;

        Stopwatch start = Stopwatch.StartNew();
        using IEnumerator<SKBitmap> enumerator = video.Frames.GetEnumerator();
        while (Frame < lastFrame)
        {
            int desiredFrame = (int)Math.Min(start.Elapsed.TotalSeconds * frameRate, lastFrame);
            SKBitmap? bitmap = null;
            while (Frame < desiredFrame && enumerator.MoveNext())
            {
                bitmap = enumerator.Current;
                Frame++;
            }

            if (bitmap != null)
            {
                PushFrame(bitmap);
            }

            int msUntilNextFrame = (int)(((Frame + 1) / frameRate - start.Elapsed.TotalSeconds) * 1000.0);

            if (msUntilNextFrame > 0)
            {
                Thread.Sleep(msUntilNextFrame);
            }
        }
    }

    private readonly Dictionary<ButtonRef, bool> _states = new();
    
    private void PushFrame(SKBitmap bitmap)
    {
        for (int x = 0; x < bitmap.Width; x++)
        {
            for (int y = 0; y < bitmap.Height; y++)
            {
                if (Mapping.Mapping.Inverse.TryGetValue(Vec2I.of(x, y), out var btn))
                {
                    SKColor color = bitmap.GetPixel(x, y);
                    color.ToHsl(out float _, out float _, out float l);
                    _states[btn] = l >= .5f;  
                } 
            }
        }
        
        Panel.Leds.SetAllStates(_states);
    }
}