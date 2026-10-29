using ValoRecorder.Core.Sessions;

namespace ValoRecorder.Core.Tests;

public sealed class RecordingStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ValoRecorderTests", Guid.NewGuid().ToString("N"));

    public RecordingStorageTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>指定サイズの動画を持つ試合フォルダを作る（minutesAgo が大きいほど古い）。</summary>
    private MatchSession AddSession(string name, int minutesAgo, int videoBytes, SessionStatus status = SessionStatus.Ready)
    {
        var session = new MatchSession
        {
            Directory = Path.Combine(_root, name),
            DetectedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo),
            Status = status,
        };
        Directory.CreateDirectory(session.Directory);
        session.Save();
        File.WriteAllBytes(session.VideoPath, new byte[videoBytes]);
        return session;
    }

    private string[] RemainingNames() =>
        MatchSession.LoadAll(_root).Select(s => Path.GetFileName(s.Directory)).Order().ToArray();

    [Fact]
    public void UnderLimit_DeletesNothing()
    {
        AddSession("a", 30, 1000);
        AddSession("b", 20, 1000);

        var deleted = RecordingStorage.EnforceLimit(_root, 1_000_000, _ => true);

        Assert.Empty(deleted);
        Assert.Equal(["a", "b"], RemainingNames());
    }

    [Fact]
    public void OverLimit_DeletesOldestFirstUntilUnderLimit()
    {
        AddSession("old", 30, 1000);
        var mid = AddSession("mid", 20, 1000);
        var @new = AddSession("new", 10, 1000);

        // 2 試合ぶんまで（session.json は日時の桁数で試合ごとにサイズが変わるので実サイズで決める）
        var limit = RecordingStorage.GetSize(mid) + RecordingStorage.GetSize(@new);
        var deleted = RecordingStorage.EnforceLimit(_root, limit, _ => true);

        Assert.Equal(["old"], deleted.Select(s => Path.GetFileName(s.Directory)));
        Assert.Equal(["mid", "new"], RemainingNames());
        Assert.False(Directory.Exists(Path.Combine(_root, "old")));
    }

    [Fact]
    public void BookmarkedSessions_AreKept()
    {
        var old = AddSession("old", 30, 1000);
        old.SetBookmarked(true);
        AddSession("mid", 20, 1000);
        var @new = AddSession("new", 10, 1000);

        var limit = RecordingStorage.GetSize(old) + RecordingStorage.GetSize(@new);
        var deleted = RecordingStorage.EnforceLimit(_root, limit, _ => true);

        Assert.Equal(["mid"], deleted.Select(s => Path.GetFileName(s.Directory)));
        Assert.Equal(["new", "old"], RemainingNames());
    }

    [Fact]
    public void ProtectedSessions_AreKept_EvenIfStillOverLimit()
    {
        AddSession("old", 30, 1000, SessionStatus.FetchingDetails);
        AddSession("new", 10, 1000);

        var deleted = RecordingStorage.EnforceLimit(_root, 0, s => s.Status != SessionStatus.FetchingDetails);

        Assert.Equal(["new"], deleted.Select(s => Path.GetFileName(s.Directory)));
        Assert.Equal(["old"], RemainingNames());
    }

    [Fact]
    public void Bookmark_RoundTrips()
    {
        var session = AddSession("a", 0, 10);
        Assert.False(session.IsBookmarked);

        session.SetBookmarked(true);
        Assert.True(MatchSession.Load(session.Directory).IsBookmarked);

        session.SetBookmarked(false);
        Assert.False(MatchSession.Load(session.Directory).IsBookmarked);
    }
}
