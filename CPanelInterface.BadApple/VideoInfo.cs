using System.Drawing;
using SkiaSharp;

namespace CPanelInterface.BadApple;

public record struct VideoInfo
{
    public required IEnumerable<SKBitmap> Frames { get; set; }
    public required int Fps { get; set; }
    public required int NumFrames { get; set; }
}