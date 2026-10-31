using ValoCapture.Core.Sessions;

namespace ValoCapture.Core.Tests;

public sealed class ClipStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ValoCaptureTests", Guid.NewGuid().ToString("N"));

    public ClipStorageTests() => Directory.CreateDirectory(ClipStorage.GetDirectory(_root));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly MatchSession Session = new()
    {
        MapId = "/Game/Maps/Ascent/Ascent",
        DetectedAt = new DateTimeOffset(2026, 10, 31, 21, 30, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 31, 21, 30, 0))),
    };

    [Fact]
    public void NewPath_NamesByMapDateAndRange()
    {
        var path = ClipStorage.NewPath(_root, Session, TimeSpan.FromSeconds(83.4), TimeSpan.FromMinutes(65) + TimeSpan.FromSeconds(12));

        Assert.Equal(Path.Combine(_root, "clips", "アセント_20261031-2130_0123-6512.mp4"), path);
    }

    [Fact]
    public void NewPath_AddsNumberWhenNameExists()
    {
        var first = ClipStorage.NewPath(_root, Session, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20));
        File.WriteAllBytes(first, []);
        var second = ClipStorage.NewPath(_root, Session, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20));
        File.WriteAllBytes(second, []);

        Assert.EndsWith("_0010-0020_2.mp4", second);
        Assert.EndsWith("_0010-0020_3.mp4", ClipStorage.NewPath(_root, Session, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void ClipsFolder_IsNotListedAsMatch()
    {
        File.WriteAllBytes(ClipStorage.NewPath(_root, Session, TimeSpan.Zero, TimeSpan.FromSeconds(5)), [1, 2, 3]);

        Assert.Empty(MatchSession.LoadAll(_root));
        Assert.Equal(0, RecordingStorage.GetTotalSize(_root));
    }
}
