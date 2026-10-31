using System.Buffers.Binary;
using System.Text;

namespace ValoCapture.Core.Recording;

/// <summary>
/// 断片化 MP4（録画中に異常終了してもそこまで再生できる形式）を、再エンコードせずに通常の MP4 に変換する。
/// 断片化 MP4 は Windows の再生エンジンで奥へのシークが遅く、シークが重なると違う場面が出たり先頭に戻ったりする
/// （40 分の録画で 22 分あたりから先）。通常の MP4 なら全体の目次（moov）があるのですぐにシークできる。
/// </summary>
public static class Mp4Defragmenter
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
    /// <exception cref="InvalidDataException">断片化 MP4 として読めない。</exception>
    public static void Defragment(string sourcePath, string destinationPath)
    {
        using var source = OpenRead(sourcePath);
        var movie = ReadFragments(source);

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

        output.Write(BuildMoov(movie));
    }

    // 再生中でも読めるようにする。削除は許さない（変換中に容量制限やごみ箱への移動で消されないように）
    private static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024);

    // ---- 読み込み ----

    private sealed class Movie
    {
        public byte[] Ftyp = [];
        public byte[] Moov = [];
        public Dictionary<uint, Track> Tracks = [];
        /// <summary>サンプルのデータの塊（trun ごと）。ファイル内の順。</summary>
        public List<Chunk> Chunks = [];
    }

    private sealed class Track
    {
        public required uint Id;
        public required uint Timescale;
        public uint DefaultDuration, DefaultSize, DefaultFlags;
        public List<Sample> Samples = [];
        public List<Chunk> Chunks = [];
        public ulong Duration => Samples.Aggregate(0UL, (sum, s) => sum + s.Duration);
    }

    private readonly record struct Sample(uint Size, uint Duration, bool IsSync, int CompositionOffset);

    private sealed class Chunk
    {
        public required long SourceOffset;
        public required long Length;
        public required int SampleCount;
        public long NewOffset;
    }

    private static Movie ReadFragments(FileStream file)
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
                    if (!ReadMoof(movie, ReadRange(file, box.Start, box.End), box.Start, file.Length)) return movie;
                    break;
            }
        }
        if (movie.Ftyp.Length == 0 || movie.Tracks.Count == 0) throw new InvalidDataException("MP4 として読めません");
        return movie;
    }

    private static void ReadTracks(Movie movie)
    {
        var moov = movie.Moov;
        foreach (var trak in Children(moov, 8, moov.Length).Where(b => b.Type == "trak"))
        {
            var tkhd = Find(moov, trak, "tkhd");
            var mdhd = Find(moov, Find(moov, trak, "mdia"), "mdhd");
            var id = BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan(tkhd.DataStart + (moov[tkhd.DataStart] == 1 ? 20 : 12)));
            var timescale = BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan(mdhd.DataStart + (moov[mdhd.DataStart] == 1 ? 20 : 12)));
            movie.Tracks[id] = new Track { Id = id, Timescale = timescale };
        }

        var mvex = Children(moov, 8, moov.Length).FirstOrDefault(b => b.Type == "mvex");
        if (mvex.Type is null) throw new InvalidDataException("断片化 MP4 ではありません");
        foreach (var trex in Children(moov, mvex.DataStart, mvex.End).Where(b => b.Type == "trex"))
        {
            var d = moov.AsSpan(trex.DataStart);
            if (!movie.Tracks.TryGetValue(BinaryPrimitives.ReadUInt32BigEndian(d[4..]), out var track)) continue;
            track.DefaultDuration = BinaryPrimitives.ReadUInt32BigEndian(d[12..]);
            track.DefaultSize = BinaryPrimitives.ReadUInt32BigEndian(d[16..]);
            track.DefaultFlags = BinaryPrimitives.ReadUInt32BigEndian(d[20..]);
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
                        samples.Add(new Sample(sSize, sDuration, (sFlags & 0x10000) == 0, cto));
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

    // ---- 書き出し ----

    private static byte[] BuildMoov(Movie movie)
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
                    WriteTrak(w, moov, child, track, MovieDuration(track));
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
    private static void WriteTrak(BoxWriter w, byte[] moov, BoxRef trak, Track track, ulong duration)
    {
        var mediaDuration = track.Duration;
        var trakPos = w.Begin("trak");
        foreach (var child in Children(moov, trak.DataStart, trak.End))
        {
            if (child.Type == "tkhd") w.Write(WithDuration(moov, child, duration, v1Offset: 28, v0Offset: 20));
            else if (child.Type == "mdia") WriteMdia(w, moov, child, track, mediaDuration);
            else w.Write(moov.AsSpan(child.Start, child.End - child.Start));
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
