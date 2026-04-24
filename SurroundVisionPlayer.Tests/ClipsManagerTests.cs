using Xunit;
using SurroundVisionPlayer.Logic;

namespace SurroundVisionPlayer.Tests;

public class ClipsManagerTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public ClipsManagerTests() => Directory.CreateDirectory(_tmp);
    public void Dispose()       => Directory.Delete(_tmp, recursive: true);

    private string ClipsDir => ClipsManager.ClipsFolder(_tmp);
    private void MakeClipsDir() => Directory.CreateDirectory(ClipsDir);
    private void Touch(string name) => File.WriteAllBytes(Path.Combine(ClipsDir, name), []);

    // ── ClipsFolder ───────────────────────────────────────────────────────────

    [Fact]
    public void ClipsFolder_ReturnsClipsSubdirectory()
        => Assert.Equal(Path.Combine(_tmp, "clips"), ClipsManager.ClipsFolder(_tmp));

    // ── ScanClips ─────────────────────────────────────────────────────────────

    [Fact]
    public void ScanClips_NoClipsFolder_ReturnsEmpty()
        => Assert.Empty(ClipsManager.ScanClips(_tmp));

    [Fact]
    public void ScanClips_EmptyClipsFolder_ReturnsEmpty()
    {
        MakeClipsDir();
        Assert.Empty(ClipsManager.ScanClips(_tmp));
    }

    [Fact]
    public void ScanClips_ReturnsMp4Files()
    {
        MakeClipsDir();
        Touch("clip_2026-04-16_15-20-00_to_15-22-00_FRONT.mp4");
        Touch("clip_2026-04-16_15-25-00_to_15-26-00_FRONT.mp4");
        Assert.Equal(2, ClipsManager.ScanClips(_tmp).Count);
    }

    [Fact]
    public void ScanClips_IgnoresNonMp4Files()
    {
        MakeClipsDir();
        File.WriteAllBytes(Path.Combine(ClipsDir, "readme.txt"), []);
        Touch("clip_2026-04-16_15-20-00_to_15-22-00_FRONT.mp4");
        Assert.Single(ClipsManager.ScanClips(_tmp));
    }

    [Fact]
    public void ScanClips_SortedByFilenameAscending()
    {
        MakeClipsDir();
        Touch("clip_2026-04-16_15-25-00_to_15-26-00_FRONT.mp4");
        Touch("clip_2026-04-16_15-20-00_to_15-22-00_FRONT.mp4");
        var clips = ClipsManager.ScanClips(_tmp);
        Assert.True(string.Compare(clips[0].FileName, clips[1].FileName, StringComparison.Ordinal) < 0);
    }

    // ── BuildClipName ─────────────────────────────────────────────────────────

    [Fact]
    public void BuildClipName_SinglePart_CorrectTimes()
    {
        // 15:18:19 + 2:30 = 15:20:49,  + 4:00 = 15:22:19
        var name = ClipsManager.BuildClipName("2026_04_16_T_15_18_19", 150_000, 240_000, "FRONT");
        Assert.Equal("clip_2026-04-16_15-20-49_to_15-22-19_FRONT.mp4", name);
    }

    [Fact]
    public void BuildClipName_SinglePart_NoPartSuffix()
    {
        var name = ClipsManager.BuildClipName("2026_04_16_T_15_18_19", 0, 60_000, "FRONT", 0);
        Assert.DoesNotContain("_p", name);
    }

    [Fact]
    public void BuildClipName_MultiPart_HasPartSuffix()
    {
        var name = ClipsManager.BuildClipName("2026_04_16_T_15_18_19", 150_000, 540_000, "FRONT", 1);
        Assert.EndsWith("_p1.mp4", name);
    }

    [Fact]
    public void BuildClipName_ContainsAngle()
    {
        var name = ClipsManager.BuildClipName("2026_04_16_T_15_18_19", 0, 60_000, "LEFT");
        Assert.Contains("LEFT", name);
    }

    [Fact]
    public void BuildClipName_StartsWithClipPrefix()
    {
        var name = ClipsManager.BuildClipName("2026_04_16_T_15_18_19", 0, 60_000, "FRONT");
        Assert.StartsWith("clip_", name);
    }

    [Fact]
    public void BuildClipName_EndsWithMp4()
    {
        var name = ClipsManager.BuildClipName("2026_04_16_T_15_18_19", 0, 60_000, "FRONT");
        Assert.EndsWith(".mp4", name);
    }

    // ── ClipEntry.DisplayLabel ────────────────────────────────────────────────

    [Fact]
    public void DisplayLabel_FormatsReadably()
    {
        var entry = new ClipEntry("", "clip_2026-04-16_15-20-49_to_15-22-19_FRONT", DateTime.Now);
        Assert.Equal("15:20:49 → 15:22:19  FRONT", entry.DisplayLabel);
    }

    [Fact]
    public void DisplayLabel_MultiPart_IncludesPartLabel()
    {
        var entry = new ClipEntry("", "clip_2026-04-16_15-20-49_to_15-22-19_FRONT_p1", DateTime.Now);
        Assert.Contains("p1", entry.DisplayLabel);
    }
}
