using System.Buffers.Binary;
using System.Text;

namespace ValoCapture.Core.Recording;

/// <summary>
/// MP4 を再エンコードせずに書き直す。
/// <list type="bullet">
/// <item>断片化 MP4（録画中に異常終了してもそこまで再生できる形式）を通常の MP4 に変換する。
/// 断片化 MP4 は Windows の再生エンジンで奥へのシークが遅く、シークが重なると違う場面が出たり先頭に戻ったりする
/// （40 分の録画で 22 分あたりから先）。通常の MP4 なら全体の目次（moov）があるのですぐにシークできる。</item>
/// <item>一部の範囲をクリップとして切り出す。</item>
/// </list>
/// </summary>
public static class Mp4Remuxer
{
    private const int CopyBufferSize = 4 * 1024 * 1024;

    /// <summary>断片化 MP4 か（moov に mvex がある）。MP4 として読めなければ false。</summary>
    public static bool IsFragmented(string path)
    {
        using var file = OpenRead(path);
        foreach (var box in TopLevelBoxes(file))
        {
            if (box.Type != "moov") continue;
            var moov = ReadRange(file, box.Start, box.End);
            return Children(moov, 8, moov.Length).Any(b => b.Type == "mvex");
        }
        return false;
    }

    /// <summary>
    /// <paramref name="sourcePath"/> の断片化 MP4 を通常の MP4 にして <paramref name="destinationPath"/> に書く。
    /// 末尾の断片が途中で切れている場合（異常終了した録画）は、完全な断片までを書く。
    /// </summary>
    /// <exception cref="InvalidDataException">MP4 として読めない。</exception>
    public static void Defragment(string sourcePath, string destinationPath)
    {
        using var source = OpenRead(sourcePath);
        Write(source, ReadMovie(source), destinationPath, keepEdits: true);
    }

    /// <summary>
    /// <paramref name="start"/> から <paramref name="end"/> までを切り出して <paramref name="destinationPath"/> に書く。
    /// 再エンコードしないので、始まりは <paramref name="start"/> 以前で最も近いキーフレームになる。
    /// 断片化 MP4（録画中のものも）と通常の MP4 のどちらからでも切り出せる。
    /// </summary>
    /// <returns>実際に切り出した範囲（元の動画での時刻）。</returns>
    /// <exception cref="InvalidDataException">MP4 として読めない、または範囲に映像がない。</exception>
    public static (TimeSpan Start, TimeSpan End) Clip(string sourcePath, string destinationPath, TimeSpan start, TimeSpan end)
    {
        using var source = OpenRead(sourcePath);
        var (clip, range) = Select(ReadMovie(source), start, end);
        Write(source, clip, destinationPath, keepEdits: false);
        return range;
    }

    // 再生中・録画中でも読めるようにする。削除は許さない（変換中に容量制限やごみ箱への移動で消されないように）
    private static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024);

    // ---- 読み込み ----

    private sealed class Movie
    {
        public byte[] Ftyp = [];
        public byte[] Moov = [];
        public Dictionary<uint, Track> Tracks = [];
        /// <summary>サンプルのデータの塊。ファイル内の順。</summary>
        public List<Chunk> Chunks = [];
    }

    private sealed class Track
    {
        public required uint Id;
        public required uint Timescale;
        /// <summary>hdlr の種類（映像は vide、音声は soun）。</summary>
        public required string Handler;
        public uint DefaultDuration, DefaultSize, DefaultFlags;
        public List<Sample> Samples = [];
        public List<Chunk> Chunks = [];
        public ulong Duration => Samples.Aggregate(0UL, (sum, s) => sum + s.Duration);
    }

    /// <param name="Offset">元のファイルでのデータの位置。</param>
    private readonly record struct Sample(uint Size, uint Duration, bool IsSync, int CompositionOffset, long Offset);

    private sealed class Chunk
    {
        public required long SourceOffset;
        public required long Length;
        public required int SampleCount;
        public long NewOffset;
    }

    private static Movie ReadMovie(FileStream file)
    {
        var movie = new Movie();
        foreach (var box in TopLevelBoxes(file))
        {
            switch (box.Type)
            {
                case "ftyp":
                    movie.Ftyp = ReadRange(file, box.Start, box.End);
                    break;
                case "moov":
                    movie.Moov = ReadRange(file, box.Start, box.End);
                    ReadTracks(movie);
                    break;
                case "moof":
                    if (movie.Tracks.Count == 0) throw new InvalidDataException("moov より前に moof があります");
                    if (!ReadMoof(movie, ReadRange(file, box.Start, box.End), box.Start, file.Length)) return SortChunks(movie);
                    break;
            }
        }
        if (movie.Ftyp.Length == 0 || movie.Tracks.Count == 0) throw new InvalidDataException("MP4 として読めません");
        return SortChunks(movie);
    }

    private static Movie SortChunks(Movie movie)
    {
        movie.Chunks.Sort((a, b) => a.SourceOffset.CompareTo(b.SourceOffset));
        return movie;
    }

    /// <summary>トラックを読む。断片化 MP4 なら mvex の既定値を、通常の MP4 ならサンプルの表を読む。</summary>
    private static void ReadTracks(Movie movie)
    {
        var moov = movie.Moov;
        var mvex = Children(moov, 8, moov.Length).FirstOrDefault(b => b.Type == "mvex");
        foreach (var trak in Children(moov, 8, moov.Length).Where(b => b.Type == "trak"))
        {
            var tkhd = Find(moov, trak, "tkhd");
            var mdia = Find(moov, trak, "mdia");
            var mdhd = Find(moov, mdia, "mdhd");
            var hdlr = Find(moov, mdia, "hdlr");
            var track = new Track
            {
                Id = BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan(tkhd.DataStart + (moov[tkhd.DataStart] == 1 ? 20 : 12))),
                Timescale = BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan(mdhd.DataStart + (moov[mdhd.DataStart] == 1 ? 20 : 12))),
                Handler = Encoding.ASCII.GetString(moov, hdlr.DataStart + 8, 4),
            };
            movie.Tracks[track.Id] = track;
            if (mvex.Type is null) ReadSampleTables(movie, track, Find(moov, Find(moov, mdia, "minf"), "stbl"));
        }

        if (mvex.Type is null) return;
        foreach (var trex in Children(moov, mvex.DataStart, mvex.End).Where(b => b.Type == "trex"))
        {
            var d = moov.AsSpan(trex.DataStart);
            if (!movie.Tracks.TryGetValue(BinaryPrimitives.ReadUInt32BigEndian(d[4..]), out var track)) continue;
            track.DefaultDuration = BinaryPrimitives.ReadUInt32BigEndian(d[12..]);
            track.DefaultSize = BinaryPrimitives.ReadUInt32BigEndian(d[16..]);
            track.DefaultFlags = BinaryPrimitives.ReadUInt32BigEndian(d[20..]);
        }
    }

    /// <summary>通常の MP4 の stbl（stts / ctts / stss / stsz / stsc / stco・co64）からサンプルと塊を読む。</summary>
    private static void ReadSampleTables(Movie movie, Track track, BoxRef stbl)
    {
        var moov = movie.Moov;
        BoxRef? Optional(string type)
        {
            var box = Children(moov, stbl.DataStart, stbl.End).FirstOrDefault(b => b.Type == type);
            return box.Type is null ? null : box;
        }
        uint U32(int pos) => BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan(pos));
        // 表の先頭の位置と項目数（version・flags と、項目数の前にある skip バイトの後）
        (int Pos, int Count) Table(BoxRef box, int skip = 0) => (box.DataStart + 8 + skip, (int)U32(box.DataStart + 4 + skip));

        var stsz = Find(moov, stbl, "stsz");
        var constantSize = U32(stsz.DataStart + 4);
        var (sizePos, sampleCount) = Table(stsz, 4);

        var durations = new uint[sampleCount];
        var (p, n) = Table(Find(moov, stbl, "stts"));
        for (int e = 0, i = 0; e < n; e++, p += 8)
            for (var k = U32(p); k > 0 && i < sampleCount; k--) durations[i++] = U32(p + 4);

        var compositionOffsets = new int[sampleCount];
        if (Optional("ctts") is { } ctts)
        {
            (p, n) = Table(ctts);
            for (int e = 0, i = 0; e < n; e++, p += 8)
                for (var k = U32(p); k > 0 && i < sampleCount; k--) compositionOffsets[i++] = (int)U32(p + 4);
        }

        // stss がなければすべてキーフレーム
        bool[]? sync = null;
        if (Optional("stss") is { } stss)
        {
            sync = new bool[sampleCount];
            (p, n) = Table(stss);
            for (var e = 0; e < n; e++, p += 4)
                if (U32(p) is var number && number >= 1 && number <= sampleCount) sync[number - 1] = true;
        }

        long[] chunkOffsets;
        if (Optional("co64") is { } co64)
        {
            (p, n) = Table(co64);
            chunkOffsets = Enumerable.Range(0, n).Select(i => (long)BinaryPrimitives.ReadUInt64BigEndian(moov.AsSpan(p + i * 8))).ToArray();
        }
        else
        {
            (p, n) = Table(Find(moov, stbl, "stco"));
            chunkOffsets = Enumerable.Range(0, n).Select(i => (long)U32(p + i * 4)).ToArray();
        }

        // stsc は (最初の塊の番号, 塊ごとのサンプル数, …) の並び。次の項目の塊の手前まで同じ数が続く
        var (stscPos, stscCount) = Table(Find(moov, stbl, "stsc"));
        var sample = 0;
        for (var e = 0; e < stscCount && sample < sampleCount; e++)
        {
            var firstChunk = (int)U32(stscPos + e * 12) - 1;
            var endChunk = e + 1 < stscCount ? (int)U32(stscPos + (e + 1) * 12) - 1 : chunkOffsets.Length;
            var perChunk = (int)U32(stscPos + e * 12 + 4);
            for (var c = Math.Max(firstChunk, 0); c < Math.Min(endChunk, chunkOffsets.Length) && sample < sampleCount; c++)
            {
                var offset = chunkOffsets[c];
                var chunk = new Chunk { SourceOffset = offset, Length = 0, SampleCount = 0 };
                for (var k = 0; k < perChunk && sample < sampleCount; k++, sample++)
                {
                    var size = constantSize != 0 ? constantSize : U32(sizePos + sample * 4);
                    track.Samples.Add(new Sample(size, durations[sample], sync?[sample] ?? true, compositionOffsets[sample], offset));
                    offset += size;
                    chunk.Length += size;
                    chunk.SampleCount++;
                }
                track.Chunks.Add(chunk);
                movie.Chunks.Add(chunk);
            }
        }
    }

    /// <returns>データがファイルの終わりで切れていたら false（それ以降は読まない）。</returns>
    private static bool ReadMoof(Movie movie, byte[] moof, long moofStart, long fileLength)
    {
        long previousTrafEnd = moofStart;
        var first = true;
        foreach (var traf in Children(moof, 8, moof.Length).Where(b => b.Type == "traf"))
        {
            Track? track = null;
            uint duration = 0, size = 0, flags = 0;
            long dataOffset = moofStart;

            foreach (var box in Children(moof, traf.DataStart, traf.End))
            {
                var d = moof.AsSpan(box.DataStart, box.End - box.DataStart);
                var boxFlags = BinaryPrimitives.ReadUInt32BigEndian(d) & 0xFFFFFF;
                var p = 4;
                if (box.Type == "tfhd")
                {
                    var id = BinaryPrimitives.ReadUInt32BigEndian(d[p..]); p += 4;
                    if (!movie.Tracks.TryGetValue(id, out track)) throw new InvalidDataException($"不明なトラック {id}");
                    (duration, size, flags) = (track.DefaultDuration, track.DefaultSize, track.DefaultFlags);
                    // base-data-offset も default-base-is-moof もなければ、2 つ目以降の traf は前の traf のデータの続き
                    dataOffset = (boxFlags & 0x20000) == 0 && !first ? previousTrafEnd : moofStart;
                    if ((boxFlags & 0x1) != 0) { dataOffset = (long)BinaryPrimitives.ReadUInt64BigEndian(d[p..]); p += 8; }
                    if ((boxFlags & 0x2) != 0) p += 4;
                    if ((boxFlags & 0x8) != 0) { duration = BinaryPrimitives.ReadUInt32BigEndian(d[p..]); p += 4; }
                    if ((boxFlags & 0x10) != 0) { size = BinaryPrimitives.ReadUInt32BigEndian(d[p..]); p += 4; }
                    if ((boxFlags & 0x20) != 0) { flags = BinaryPrimitives.ReadUInt32BigEndian(d[p..]); }
                    previousTrafEnd = dataOffset;
                }
                else if (box.Type == "trun" && track is not null)
                {
                    var count = BinaryPrimitives.ReadUInt32BigEndian(d[p..]); p += 4;
                    var offset = previousTrafEnd;
                    if ((boxFlags & 0x1) != 0) { offset = dataOffset + BinaryPrimitives.ReadInt32BigEndian(d[p..]); p += 4; }
                    uint? firstFlags = null;
                    if ((boxFlags & 0x4) != 0) { firstFlags = BinaryPrimitives.ReadUInt32BigEndian(d[p..]); p += 4; }

                    var samples = new List<Sample>((int)count);
                    long length = 0;
                    for (var i = 0; i < count; i++)
                    {
                        var (sDuration, sSize, sFlags, cto) = (duration, size, i == 0 && firstFlags is { } ff ? ff : flags, 0);
                        if ((boxFlags & 0x100) != 0) { sDuration = BinaryPrimitives.ReadUInt32BigEndian(d[p..]); p += 4; }
                        if ((boxFlags & 0x200) != 0) { sSize = BinaryPrimitives.ReadUInt32BigEndian(d[p..]); p += 4; }
                        if ((boxFlags & 0x400) != 0) { sFlags = BinaryPrimitives.ReadUInt32BigEndian(d[p..]); p += 4; }
                        if ((boxFlags & 0x800) != 0) { cto = BinaryPrimitives.ReadInt32BigEndian(d[p..]); p += 4; }
                        // sample_is_non_sync_sample
                        samples.Add(new Sample(sSize, sDuration, (sFlags & 0x10000) == 0, cto, offset + length));
                        length += sSize;
                    }

                    if (offset + length > fileLength) return false;
                    if (count > 0)
                    {
                        var chunk = new Chunk { SourceOffset = offset, Length = length, SampleCount = (int)count };
                        track.Samples.AddRange(samples);
                        track.Chunks.Add(chunk);
                        movie.Chunks.Add(chunk);
                    }
                    previousTrafEnd = offset + length;
                }
            }
            first = false;
        }
        return true;
    }

    // ---- 切り出し ----

    /// <summary>
    /// 範囲のサンプルだけを持つ <see cref="Movie"/> を作る。映像は <paramref name="start"/> 以前で最も近いキーフレームから
    /// <paramref name="end"/> の手前まで、ほかのトラック（音声）は映像と同じ時刻の範囲にする。
    /// </summary>
    private static (Movie Clip, (TimeSpan Start, TimeSpan End) Range) Select(Movie movie, TimeSpan start, TimeSpan end)
    {
        var video = movie.Tracks.Values.FirstOrDefault(t => t.Handler == "vide" && t.Samples.Count > 0)
            ?? throw new InvalidDataException("映像のトラックがありません");

        var (startTicks, endTicks) = (ToTicks(video, start), ToTicks(video, end));
        if (endTicks <= startTicks || startTicks >= video.Duration) throw new InvalidDataException("切り出す範囲に映像がありません");
        ulong time = 0, firstTime = 0, lastEnd = 0;
        int first = 0, last = -1;
        for (var i = 0; i < video.Samples.Count && time < endTicks; time += video.Samples[i].Duration, i++)
        {
            if (video.Samples[i].IsSync && time <= startTicks) (first, firstTime) = (i, time);
            (last, lastEnd) = (i, time + video.Samples[i].Duration);
        }
        if (last < first) throw new InvalidDataException("切り出す範囲に映像がありません");
        var (clipStart, clipEnd) = (FromTicks(video, firstTime), FromTicks(video, lastEnd));

        var clip = new Movie { Ftyp = movie.Ftyp, Moov = movie.Moov };
        foreach (var track in movie.Tracks.Values)
        {
            var selected = new Track { Id = track.Id, Timescale = track.Timescale, Handler = track.Handler };
            if (track == video)
            {
                selected.Samples.AddRange(video.Samples.GetRange(first, last - first + 1));
            }
            else
            {
                var (from, to) = (ToTicks(track, clipStart), ToTicks(track, clipEnd));
                time = 0;
                foreach (var s in track.Samples)
                {
                    if (time >= to) break;
                    if (time >= from) selected.Samples.Add(s);
                    time += s.Duration;
                }
            }

            // 元のファイルで続いているサンプルを 1 つの塊にする
            Chunk? chunk = null;
            foreach (var s in selected.Samples)
            {
                if (chunk is null || chunk.SourceOffset + chunk.Length != s.Offset)
                {
                    chunk = new Chunk { SourceOffset = s.Offset, Length = 0, SampleCount = 0 };
                    selected.Chunks.Add(chunk);
                    clip.Chunks.Add(chunk);
                }
                chunk.Length += s.Size;
                chunk.SampleCount++;
            }
            clip.Tracks[track.Id] = selected;
        }
        return (SortChunks(clip), (clipStart, clipEnd));
    }

    private static ulong ToTicks(Track track, TimeSpan t) =>
        t <= TimeSpan.Zero ? 0 : (ulong)Math.Round(t.TotalSeconds * track.Timescale);

    private static TimeSpan FromTicks(Track track, ulong ticks) =>
        track.Timescale == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double)ticks / track.Timescale);

    // ---- 書き出し ----

    /// <param name="keepEdits">編集リスト（edts）を残すか。切り出したものでは元の動画の先頭に合わせた値になるので捨てる。</param>
    private static void Write(FileStream source, Movie movie, string destinationPath, bool keepEdits)
    {
        using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize);
        output.Write(movie.Ftyp);

        // mdat はサイズが 4GB を超えるので 64 ビットのヘッダーにする
        var payload = movie.Chunks.Sum(c => c.Length);
        Span<byte> header = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(header, 1);
        Encoding.ASCII.GetBytes("mdat", header[4..8]);
        BinaryPrimitives.WriteUInt64BigEndian(header[8..], (ulong)(16 + payload));
        output.Write(header);

        var buffer = new byte[CopyBufferSize];
        foreach (var chunk in movie.Chunks)
        {
            chunk.NewOffset = output.Position;
            source.Position = chunk.SourceOffset;
            for (var left = chunk.Length; left > 0;)
            {
                var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                if (read == 0) throw new InvalidDataException("動画のデータが途中で切れています");
                output.Write(buffer, 0, read);
                left -= read;
            }
        }

        output.Write(BuildMoov(movie, keepEdits));
    }

    private static byte[] BuildMoov(Movie movie, bool keepEdits)
    {
        var moov = movie.Moov;
        var mvhd = Find(moov, new BoxRef("moov", 0, 8, moov.Length), "mvhd");
        var movieTimescale = BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan(mvhd.DataStart + (moov[mvhd.DataStart] == 1 ? 20 : 12)));

        ulong MovieDuration(Track t) => t.Timescale == 0 ? 0 : t.Duration * movieTimescale / t.Timescale;

        var w = new BoxWriter();
        var moovPos = w.Begin("moov");
        w.Write(WithDuration(moov, mvhd, movie.Tracks.Values.Select(MovieDuration).DefaultIfEmpty().Max(), v1Offset: 24, v0Offset: 16));
        foreach (var child in Children(moov, 8, moov.Length))
        {
            switch (child.Type)
            {
                case "mvhd" or "mvex":
                    break;
                case "trak":
                    var tkhd = Find(moov, child, "tkhd");
                    var track = movie.Tracks[BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan(tkhd.DataStart + (moov[tkhd.DataStart] == 1 ? 20 : 12)))];
                    WriteTrak(w, moov, child, track, MovieDuration(track), keepEdits);
                    break;
                default:
                    w.Write(moov.AsSpan(child.Start, child.End - child.Start));
                    break;
            }
        }
        w.End(moovPos);
        return w.ToArray();
    }

    /// <param name="duration">トラックの長さ（ムービーのタイムスケール）。</param>
    private static void WriteTrak(BoxWriter w, byte[] moov, BoxRef trak, Track track, ulong duration, bool keepEdits)
    {
        var mediaDuration = track.Duration;
        var trakPos = w.Begin("trak");
        foreach (var child in Children(moov, trak.DataStart, trak.End))
        {
            if (child.Type == "tkhd") w.Write(WithDuration(moov, child, duration, v1Offset: 28, v0Offset: 20));
            else if (child.Type == "mdia") WriteMdia(w, moov, child, track, mediaDuration);
            else if (child.Type != "edts" || keepEdits) w.Write(moov.AsSpan(child.Start, child.End - child.Start));
        }
        w.End(trakPos);
    }

    private static void WriteMdia(BoxWriter w, byte[] moov, BoxRef mdia, Track track, ulong mediaDuration)
    {
        var mdiaPos = w.Begin("mdia");
        foreach (var child in Children(moov, mdia.DataStart, mdia.End))
        {
            if (child.Type == "mdhd") w.Write(WithDuration(moov, child, mediaDuration, v1Offset: 24, v0Offset: 16));
            else if (child.Type == "minf")
            {
                var minfPos = w.Begin("minf");
                foreach (var m in Children(moov, child.DataStart, child.End))
                {
                    if (m.Type == "stbl") WriteStbl(w, moov, m, track);
                    else w.Write(moov.AsSpan(m.Start, m.End - m.Start));
                }
                w.End(minfPos);
            }
            else w.Write(moov.AsSpan(child.Start, child.End - child.Start));
        }
        w.End(mdiaPos);
    }

    private static void WriteStbl(BoxWriter w, byte[] moov, BoxRef stbl, Track track)
    {
        var samples = track.Samples;
        var stblPos = w.Begin("stbl");
        var stsd = Find(moov, stbl, "stsd");
        w.Write(moov.AsSpan(stsd.Start, stsd.End - stsd.Start));

        var stts = Runs(samples.Select(s => s.Duration));
        var pos = w.BeginFull("stts", 0, 0);
        w.U32((uint)stts.Count);
        foreach (var (count, value) in stts) { w.U32(count); w.U32(value); }
        w.End(pos);

        if (samples.Any(s => s.CompositionOffset != 0))
        {
            var ctts = Runs(samples.Select(s => s.CompositionOffset));
            pos = w.BeginFull("ctts", (byte)(samples.Any(s => s.CompositionOffset < 0) ? 1 : 0), 0);
            w.U32((uint)ctts.Count);
            foreach (var (count, value) in ctts) { w.U32(count); w.U32((uint)value); }
            w.End(pos);
        }

        // すべてキーフレームなら stss は書かない
        if (samples.Any(s => !s.IsSync))
        {
            pos = w.BeginFull("stss", 0, 0);
            w.U32((uint)samples.Count(s => s.IsSync));
            for (var i = 0; i < samples.Count; i++)
                if (samples[i].IsSync) w.U32((uint)i + 1);
            w.End(pos);
        }

        var stsc = Runs(track.Chunks.Select(c => c.SampleCount));
        pos = w.BeginFull("stsc", 0, 0);
        w.U32((uint)stsc.Count);
        uint chunkNumber = 1;
        foreach (var (count, perChunk) in stsc)
        {
            w.U32(chunkNumber); w.U32((uint)perChunk); w.U32(1);
            chunkNumber += count;
        }
        w.End(pos);

        pos = w.BeginFull("stsz", 0, 0);
        w.U32(0);
        w.U32((uint)samples.Count);
        foreach (var s in samples) w.U32(s.Size);
        w.End(pos);

        pos = w.BeginFull("co64", 0, 0);
        w.U32((uint)track.Chunks.Count);
        foreach (var c in track.Chunks) w.U64((ulong)c.NewOffset);
        w.End(pos);

        w.End(stblPos);
    }

    /// <summary>同じ値の連続を (個数, 値) にまとめる。</summary>
    private static List<(uint Count, T Value)> Runs<T>(IEnumerable<T> values) where T : IEquatable<T>
    {
        var runs = new List<(uint Count, T Value)>();
        foreach (var v in values)
        {
            if (runs.Count > 0 && runs[^1].Value.Equals(v)) runs[^1] = (runs[^1].Count + 1, v);
            else runs.Add((1, v));
        }
        return runs;
    }

    /// <summary>mvhd / tkhd / mdhd の duration を書き換えたコピー。</summary>
    private static byte[] WithDuration(byte[] source, BoxRef box, ulong duration, int v1Offset, int v0Offset)
    {
        var copy = source.AsSpan(box.Start, box.End - box.Start).ToArray();
        var data = box.DataStart - box.Start;
        if (copy[data] == 1) BinaryPrimitives.WriteUInt64BigEndian(copy.AsSpan(data + v1Offset), duration);
        else BinaryPrimitives.WriteUInt32BigEndian(copy.AsSpan(data + v0Offset), (uint)Math.Min(duration, uint.MaxValue));
        return copy;
    }

    // ---- 箱の読み書き ----

    private readonly record struct BoxRef(string Type, int Start, int DataStart, int End);
    private readonly record struct FileBox(string Type, long Start, long End);

    /// <summary>ファイルの最上位の箱。途中で切れた箱（異常終了した録画の末尾）の手前で止まる。</summary>
    private static IEnumerable<FileBox> TopLevelBoxes(FileStream file)
    {
        var header = new byte[16];
        long pos = 0, end = file.Length;
        while (pos + 8 <= end)
        {
            file.Position = pos;
            file.ReadExactly(header, 0, 8);
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            var headerSize = 8;
            if (size == 1)
            {
                if (pos + 16 > end) yield break;
                file.ReadExactly(header, 8, 8);
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8));
                headerSize = 16;
            }
            else if (size == 0) size = end - pos;
            if (size < headerSize || pos + size > end) yield break;
            yield return new FileBox(Encoding.ASCII.GetString(header, 4, 4), pos, pos + size);
            pos += size;
        }
    }

    private static IEnumerable<BoxRef> Children(byte[] data, int start, int end)
    {
        var pos = start;
        while (pos + 8 <= end)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos));
            var headerSize = 8;
            if (size == 1) { size = (long)BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(pos + 8)); headerSize = 16; }
            else if (size == 0) size = end - pos;
            if (size < headerSize || pos + size > end) throw new InvalidDataException("MP4 の箱が壊れています");
            yield return new BoxRef(Encoding.ASCII.GetString(data, pos + 4, 4), pos, pos + headerSize, pos + (int)size);
            pos += (int)size;
        }
    }

    private static BoxRef Find(byte[] data, BoxRef parent, string type)
    {
        var box = Children(data, parent.DataStart, parent.End).FirstOrDefault(b => b.Type == type);
        return box.Type is null ? throw new InvalidDataException($"{parent.Type} に {type} がありません") : box;
    }

    private static byte[] ReadRange(FileStream file, long start, long end)
    {
        if (end - start > 64 * 1024 * 1024) throw new InvalidDataException("MP4 の箱が大きすぎます");
        var buffer = new byte[end - start];
        file.Position = start;
        file.ReadExactly(buffer);
        return buffer;
    }

    private sealed class BoxWriter
    {
        private readonly MemoryStream _stream = new();
        private readonly byte[] _scratch = new byte[8];

        public long Begin(string type)
        {
            var pos = _stream.Position;
            U32(0);
            _stream.Write(Encoding.ASCII.GetBytes(type));
            return pos;
        }

        public long BeginFull(string type, byte version, uint flags)
        {
            var pos = Begin(type);
            U32(((uint)version << 24) | flags);
            return pos;
        }

        public void End(long pos)
        {
            var end = _stream.Position;
            _stream.Position = pos;
            U32((uint)(end - pos));
            _stream.Position = end;
        }

        public void Write(ReadOnlySpan<byte> bytes) => _stream.Write(bytes);

        public void U32(uint value)
        {
            BinaryPrimitives.WriteUInt32BigEndian(_scratch, value);
            _stream.Write(_scratch, 0, 4);
        }

        public void U64(ulong value)
        {
            BinaryPrimitives.WriteUInt64BigEndian(_scratch, value);
            _stream.Write(_scratch, 0, 8);
        }

        public byte[] ToArray() => _stream.ToArray();
    }
}
