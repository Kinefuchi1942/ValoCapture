using ValoCapture.Core.Updates;

namespace ValoCapture.Core.Tests;

public sealed class ReleaseNotesTests
{
    private const string Text = """
        ## 1.4.10 (2026-11-02)
        - 二重起動したら起動済みの画面を前に出す
        - 最初の再生が先頭から始まるのを直す

        ## 1.4.9 (2026-11-01)
        - 画面左に更新のお知らせを出す

        ## 1.4.8 (2026-11-01)

        ## 1.4.7
        - クリップ作成機能を追加する
        """;

    [Fact]
    public void Parse_ReadsVersionsDatesAndChanges()
    {
        var notes = ReleaseNotes.Parse(Text.Replace("\n", "\r\n"));

        Assert.Equal(4, notes.Count);
        Assert.Equal(new Version(1, 4, 10), notes[0].Version);
        Assert.Equal(new DateOnly(2026, 11, 2), notes[0].Date);
        Assert.Equal(["二重起動したら起動済みの画面を前に出す", "最初の再生が先頭から始まるのを直す"], notes[0].Changes);
        Assert.Empty(notes[2].Changes);
        Assert.Null(notes[3].Date);
    }

    [Fact]
    public void Between_ReturnsSkippedVersionsNewestFirstWithoutEmptyOnes()
    {
        var notes = ReleaseNotes.Parse(Text);

        var shown = ReleaseNotes.Between(notes, "1.4.7", "1.4.10");

        Assert.Equal([new Version(1, 4, 10), new Version(1, 4, 9)], shown.Select(n => n.Version));
    }

    [Theory]
    [InlineData("1.4.10", "1.4.9")] // 古い版に戻した
    [InlineData("", "1.4.10")]
    public void Between_ReturnsNothingForDowngradeOrUnknownVersion(string from, string to)
    {
        Assert.Empty(ReleaseNotes.Between(ReleaseNotes.Parse(Text), from, to));
    }
}
