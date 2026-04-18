namespace SurroundVisionPlayer.Logic;

/// <summary>
/// Groups recording timestamps into continuous driving sessions (trips).
/// </summary>
public static class SessionGrouper
{
    /// <summary>
    /// Clips separated by more than this are in different sessions.
    /// 300 s = one 5-minute segment; 375 s adds 25 % headroom.
    /// </summary>
    public const int GapThresholdSeconds = 375;

    private const string TimestampFormat = "yyyy_MM_dd_T_HH_mm_ss";

    public static DateTime ParseTimestamp(string ts) =>
        DateTime.ParseExact(ts, TimestampFormat,
            System.Globalization.CultureInfo.InvariantCulture);

    public static double GapSeconds(string tsA, string tsB) =>
        (ParseTimestamp(tsB) - ParseTimestamp(tsA)).TotalSeconds;

    /// <summary>
    /// Group sorted <paramref name="timestamps"/> into sessions.
    /// Each session is a list of consecutive timestamps whose pairwise gap
    /// is ≤ <see cref="GapThresholdSeconds"/>.
    /// </summary>
    public static List<List<string>> Group(
        IReadOnlyList<string> timestamps,
        int gapThreshold = GapThresholdSeconds)
    {
        var sessions = new List<List<string>>();
        if (timestamps.Count == 0) return sessions;

        var current = new List<string> { timestamps[0] };
        for (int i = 1; i < timestamps.Count; i++)
        {
            double gap = GapSeconds(timestamps[i - 1], timestamps[i]);
            if (gap <= gapThreshold)
                current.Add(timestamps[i]);
            else
            {
                sessions.Add(current);
                current = [timestamps[i]];
            }
        }
        sessions.Add(current);
        return sessions;
    }

    /// <summary>
    /// Estimated total session duration in seconds (span + one clip length).
    /// </summary>
    public static int DurationSeconds(List<string> session)
    {
        if (session.Count == 1) return 300;
        return (int)GapSeconds(session[0], session[^1]) + 300;
    }

    /// <summary>
    /// Human-readable label: "15:18 → 16:13  (~55 min, 11 clips)"
    /// </summary>
    public static string Label(List<string> session)
    {
        var start = ParseTimestamp(session[0]);
        var end   = ParseTimestamp(session[^1]);
        int totalMin = DurationSeconds(session) / 60;
        int n = session.Count;
        string word = n == 1 ? "clip" : "clips";
        return $"{start:HH:mm} → {end:HH:mm}  (~{totalMin} min, {n} {word})";
    }
}
