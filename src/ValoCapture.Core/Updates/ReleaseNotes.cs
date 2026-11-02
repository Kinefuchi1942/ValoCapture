namespace ValoCapture.Core.Updates;

/// <summary>ある版の変更点。</summary>
public sealed record ReleaseNote(Version Version, DateOnly? Date, IReadOnlyList<string> Changes);

/// <summary>
/// アプリに同梱する変更点（ReleaseNotes.md）。リリース時に tools\publish.ps1 がコミットの件名から作る。
/// <code>
/// ## 1.4.10 (2026-11-02)
/// - 動画を開いた直後の最初の再生が先頭から始まるのを直す
/// </code>
/// </summary>
public static class ReleaseNotes
{
    public const string FileName = "ReleaseNotes.md";

    public static IReadOnlyList<ReleaseNote> Parse(string text)
    {
        var notes = new List<ReleaseNote>();
        Version? version = null;
        DateOnly? date = null;
        var changes = new List<string>();

        void Flush()
        {
            if (version is not null) notes.Add(new ReleaseNote(version, date, changes.ToArray()));
            changes.Clear();
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("## "))
            {
                Flush();
                var parts = line[3..].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                version = parts.Length > 0 && Version.TryParse(parts[0], out var v) ? v : null;
                date = parts.Length > 1 && DateOnly.TryParseExact(parts[1].Trim('(', ')'), "yyyy-MM-dd", out var d) ? d : null;
            }
            else if (line.StartsWith("- ") && version is not null)
            {
                changes.Add(line[2..].Trim());
            }
        }
        Flush();
        return notes;
    }

    /// <summary><paramref name="from"/> より新しく <paramref name="to"/> まで（両端は版の文字列）の変更点を新しい順に返す。</summary>
    public static IReadOnlyList<ReleaseNote> Between(IEnumerable<ReleaseNote> notes, string from, string to)
    {
        if (!Version.TryParse(from, out var f) || !Version.TryParse(to, out var t)) return [];
        return notes.Where(n => n.Version > f && n.Version <= t && n.Changes.Count > 0)
            .OrderByDescending(n => n.Version).ToArray();
    }
}
