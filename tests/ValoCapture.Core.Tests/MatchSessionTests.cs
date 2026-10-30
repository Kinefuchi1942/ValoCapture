using ValoCapture.Core.Sessions;

namespace ValoCapture.Core.Tests;

public sealed class MatchSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(SessionStatus.Recording, 1, true)]
    [InlineData(SessionStatus.FetchingDetails, 1, true)]
    [InlineData(SessionStatus.Ready, 1, false)]
    [InlineData(SessionStatus.Failed, 1, false)]
    // 異常終了で止まったままの古いセッションは削除してよい
    [InlineData(SessionStatus.Recording, 7, false)]
    [InlineData(SessionStatus.FetchingDetails, 7, false)]
    public void IsBusy(SessionStatus status, int hoursAgo, bool expected)
    {
        var session = new MatchSession { Status = status, DetectedAt = Now.AddHours(-hoursAgo) };

        Assert.Equal(expected, session.IsBusy(Now));
    }
}
