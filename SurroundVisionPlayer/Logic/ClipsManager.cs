using System.IO;

namespace SurroundVisionPlayer.Logic;

public sealed record ClipEntry(string FilePath, string FileName, DateTime Date)
{
    public string DisplayLabel
    {
        get
        {
            // clip_2026-04-16_15-20-49_to_15-22-19_FRONT[_p1] → "15:20:49 → 15:22:19  FRONT [p1]"
            var n = FileName.StartsWith("clip_") ? FileName[5..] : FileName;
            var parts = n.Split('_');
            if (parts.Length >= 5)
            {
                var from  = parts[1].Replace('-', ':');
                var to    = parts[3].Replace('-', ':');
                var angle = parts[4];
                var part  = parts.Length > 5 ? $"  ({parts[5]})" : "";
                return $"{from} → {to}  {angle}{part}";
            }
            return FileName;
        }
    }
}

public static class ClipsManager
{
    public static string ClipsFolder(string archiveRoot)
        => Path.Combine(archiveRoot, "clips");

    public static List<ClipEntry> ScanClips(string archiveRoot)
    {
        var dir = ClipsFolder(archiveRoot);
        if (!Directory.Exists(dir)) return [];
        return [.. Directory.EnumerateFiles(dir, "*.mp4")
            .Select(p => new ClipEntry(p, Path.GetFileNameWithoutExtension(p), File.GetLastWriteTime(p)))
            .OrderBy(e => e.FileName)];
    }

    /// <summary>
    /// Builds an output filename for an exported clip.
    /// partIndex = 0 for a single-part export; 1+ for each part of a multi-part export.
    /// </summary>
    public static string BuildClipName(
        string firstTs, long inSessionMs, long outSessionMs, string angle, int partIndex = 0)
    {
        var baseTime = SessionGrouper.ParseTimestamp(firstTs);
        var inTime   = baseTime.AddMilliseconds(inSessionMs);
        var outTime  = baseTime.AddMilliseconds(outSessionMs);

        var name = $"clip_{inTime:yyyy-MM-dd_HH-mm-ss}_to_{outTime:HH-mm-ss}_{angle}";
        if (partIndex > 0) name += $"_p{partIndex}";
        return name + ".mp4";
    }
}
