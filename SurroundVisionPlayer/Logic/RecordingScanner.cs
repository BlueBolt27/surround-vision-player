using System.IO;
using System.Text.RegularExpressions;

namespace SurroundVisionPlayer.Logic;

/// <summary>
/// Scans a SurroundVisionRecorder folder and groups MP4 files by timestamp.
/// </summary>
public static class RecordingScanner
{
    // FRONT_2026_04_16_T_15_18_19.mp4
    private static readonly Regex FilePattern = new(
        @"^(FRONT|LEFT|REAR|RIGHT)_(\d{4}_\d{2}_\d{2}_T_\d{2}_\d{2}_\d{2})\.mp4$",
        RegexOptions.Compiled);

    public const string SvrSubPath =
        @"Android\media\com.gm.ultifi.gmconnectedcameraservice\Recordings\SurroundVisionRecorder";

    public static readonly string[] Angles = ["FRONT", "LEFT", "REAR", "RIGHT"];

    /// <summary>
    /// Scan <paramref name="folder"/> and return recordings grouped by timestamp,
    /// sorted ascending.  Each inner dictionary maps angle → full file path.
    /// </summary>
    public static SortedDictionary<string, Dictionary<string, string>> Scan(string folder)
    {
        var groups = new SortedDictionary<string, Dictionary<string, string>>(
            StringComparer.Ordinal);

        if (!Directory.Exists(folder))
            return groups;

        foreach (var path in Directory.EnumerateFiles(folder, "*.mp4"))
        {
            var name = Path.GetFileName(path);
            var m = FilePattern.Match(name);
            if (!m.Success) continue;

            var angle = m.Groups[1].Value;
            var ts    = m.Groups[2].Value;

            if (!groups.TryGetValue(ts, out var angles))
            {
                angles = new Dictionary<string, string>();
                groups[ts] = angles;
            }
            angles[angle] = path;
        }
        return groups;
    }

    /// <summary>
    /// Returns true if <paramref name="folder"/> contains at least one dashcam file.
    /// </summary>
    public static bool LooksLikeSvr(string folder)
    {
        if (!Directory.Exists(folder)) return false;
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*.mp4"))
                if (FilePattern.IsMatch(Path.GetFileName(path)))
                    return true;
        }
        catch (UnauthorizedAccessException) { }
        return false;
    }

    /// <summary>
    /// Given a drive root or the SVR folder itself, locate the SVR folder.
    /// Returns null when not found.
    /// </summary>
    public static string? FindSvrFolder(string root)
    {
        if (LooksLikeSvr(root))
            return root;

        var sub = Path.Combine(root, SvrSubPath);
        if (Directory.Exists(sub))
            return sub;

        return null;
    }
}
