namespace ValoRecorder.Core.Riot;

/// <summary>
/// Riot Client が起動中に書き出す lockfile（name:pid:port:password:protocol）。
/// </summary>
public sealed record Lockfile(string Name, int Pid, int Port, string Password, string Protocol)
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Riot Games", "Riot Client", "Config", "lockfile");

    /// <summary>lockfile を読む。存在しない・読めない・形式不正なら null。</summary>
    public static Lockfile? TryRead(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return null;

        try
        {
            // Riot Client が開いたままなので共有モードで開く
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            var parts = reader.ReadToEnd().Trim().Split(':');
            if (parts.Length < 5) return null;

            return new Lockfile(parts[0], int.Parse(parts[1]), int.Parse(parts[2]), parts[3], parts[4]);
        }
        catch (IOException) { return null; }
        catch (FormatException) { return null; }
    }
}
