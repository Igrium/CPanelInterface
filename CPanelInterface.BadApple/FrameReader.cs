using SkiaSharp;

namespace CPanelInterface.BadApple;

public static class FrameReader
{
    public static IEnumerable<SKBitmap> EnumerateFolder(string folder, out int count)
    {
        string[] files = Directory.GetFiles(folder);
        count = 0;
        foreach (var file in files)
        {
            if (file.EndsWith(".png")) count++;
        }

        return _enumerateFolderInternal(files);
    }

    private static IEnumerable<SKBitmap> _enumerateFolderInternal(string[] files)
    {
        foreach (var file in files)
        {
            if (!file.EndsWith(".png")) continue;
            yield return SKBitmap.Decode(file);
        }
    }
}