using System.Globalization;
using SkiaSharp;

namespace CPanelInterface.BadApple;

public static class FrameReader
{
    /// <summary>
    /// Sorts digit runs by value, so frame2.png lands before frame10.png even when the exporter
    /// didn't zero-pad the numbers.
    /// </summary>
    private static readonly StringComparer NumericOrder =
        StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.NumericOrdering);

    public static IEnumerable<SKBitmap> EnumerateFolder(string folder, out int count)
    {
        // Directory.GetFiles returns filesystem order, so frames have to be sorted before playback.
        string[] files = Directory.GetFiles(folder, "*.png")
            .OrderBy(Path.GetFileName, NumericOrder)
            .ToArray();

        count = files.Length;
        return _enumerateFolderInternal(files);
    }

    private static IEnumerable<SKBitmap> _enumerateFolderInternal(string[] files)
    {
        foreach (var file in files)
        {
            yield return SKBitmap.Decode(file);
        }
    }
}