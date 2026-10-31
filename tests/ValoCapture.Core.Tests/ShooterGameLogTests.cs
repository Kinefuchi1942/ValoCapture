using ValoCapture.Core.Riot;

namespace ValoCapture.Core.Tests;

public class ShooterGameLogTests
{
    private const string Log = """
        Log file open, 10/30/26 23:39:58
        Time Zone UTC/GMT 9:00
        [2026.10.30-14.50.00:000][  1]LogShooterGameState: Warning: Gameplay started at local time 30.0 (server time 30.292187)
        [2026.10.30-14.56.08:767][999]LogShooterGameState: Warning: Gameplay started at local time 45.140625 (server time 45.292187)
        [2026.10.30-14.58.14:641][624]LogShooterGameState: Warning: AShooterGameState::OnRoundEnded for round '0'
        [2026.10.30-14.58.14:989][690]LogShooterGameState: Warning: Gameplay started at local time 0.054688 (server time 0.000000)
        [2026.10.30-14.58.20:000][700]LogNet: Warning: unrelated
        [2026.10.30-14.58.44:886][427]LogShooterGameState: Warning: Gameplay started at local time 30.078125 (server time 30.292187)
        """;

    private static readonly DateTimeOffset From = new(2026, 10, 30, 14, 55, 12, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 10, 30, 15, 30, 0, TimeSpan.Zero);

    [Fact]
    public void ExtractRoundLines_KeepsRoundLinesInRange()
    {
        var lines = ShooterGameLog.ExtractRoundLines(Log.Replace("\n", "\r\n"), From, To);

        Assert.Equal(4, lines.Count);
        Assert.All(lines, l => Assert.False(l.EndsWith('\r')));
        Assert.DoesNotContain(lines, l => l.Contains("14.50.00") || l.Contains("unrelated"));
    }

    [Fact]
    public void ReadRoundStarts_ReturnsCombatStartsInUtc()
    {
        var starts = ShooterGameLog.ReadRoundStarts(ShooterGameLog.ExtractRoundLines(Log, From, To));

        Assert.Equal(
            [new DateTimeOffset(2026, 10, 30, 14, 56, 8, 767, TimeSpan.Zero), new DateTimeOffset(2026, 10, 30, 14, 58, 44, 886, TimeSpan.Zero)],
            starts);
    }
}
