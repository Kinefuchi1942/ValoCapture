using System.Buffers.Binary;
using System.Text;
using ValoCapture.Core.Recording;

namespace ValoCapture.Core.Tests;

public sealed class Mp4DefragmenterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ValoCaptureTests", Guid.NewGuid().ToString("N"));

    public Mp4DefragmenterTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // ---- 断片化 MP4 を組み立てる ----

    private static byte[] Box(string type, params byte[][] content)
    {
        var size = 8 + content.Sum(c => c.Length);
        var box = new byte[size];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)size);
        Encoding.ASCII.GetBytes(type, box.AsSpan(4));
        var pos = 8;
        foreach (var c in content) { c.CopyTo(box, pos); pos += c.Length; }
        return box;
    }

    private static byte[] Full(string type, byte version, uint flags, params byte[][] content) =>
        Box(type, [U32(((uint)version << 24) | flags), .. content]);

    private static byte[] U32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); return b; }
    private static byte[] U64(ulong v) { var b = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(b, v); return b; }
    private static byte[] Zeros(int n) => new byte[n];

    private static byte[] Trak(uint id, uint timescale, string handler) =>
        Box("trak",
            Full("tkhd", 0, 3, U32(0), U32(0), U32(id), U32(0), U32(0), Zeros(60)),
            Box("mdia",
                Full("mdhd", 0, 0, U32(0), U32(0), U32(timescale), U32(0), Zeros(4)),
                Full("hdlr", 0, 0, U32(0), Encoding.ASCII.GetBytes(handler), Zeros(12), [0]),
                Box("minf",
                    Box("dinf", Full("dref", 0, 0, U32(1), Full("url ", 0, 1))),
                    Box("stbl",
                        Full("stsd", 0, 0, U32(1), Box(handler == "vide" ? "avc1" : "mp4a", Zeros(16))),
                        Full("stts", 0, 0, U32(0)),
                        Full("stsc", 0, 0, U32(0)),
                        Full("stsz", 0, 0, U32(0), U32(0)),
                        Full("stco", 0, 0, U32(0))))));

    private static readonly byte[] Ftyp = Box("ftyp", Encoding.ASCII.GetBytes("mp42"), U32(0), Encoding.ASCII.GetBytes("isom"));

    private static byte[] Moov() =>
        Box("moov",
            Full("mvhd", 1, 0, U64(0), U64(0), U32(60000), U64(0), Zeros(80)),
            Trak(1, 60000, "vide"),
            Trak(2, 48000, "soun"),
            Box("mvex",
                Full("trex", 0, 0, U32(1), U32(1), U32(0), U32(0), U32(0)),
                Full("trex", 0, 0, U32(2), U32(1), U32(1024), U32(0), U32(0))));

    /// <summary>1 トラック 1 断片。サンプルのデータは (トラック, 番号) で埋めて、変換後に場所を確かめられるようにする。</summary>
    private static byte[] Fragment(uint track, int firstSample, (uint Size, uint? Duration, bool Sync)[] samples)
    {
        var perSampleDuration = samples.Any(s => s.Duration is not null);
        var trunFlags = 0x1u | 0x200 | 0x400 | (perSampleDuration ? 0x100u : 0);
        var entries = samples.SelectMany(s => (perSampleDuration ? U32(s.Duration ?? 0) : []).Concat(U32(s.Size)).Concat(U32(s.Sync ? 0 : 0x10000u))).ToArray();
        byte[] Build(int dataOffset) =>
            Box("moof",
                Full("mfhd", 0, 0, U32(1)),
                Box("traf",
                    Full("tfhd", 0, 0x20000, U32(track)),
                    Full("trun", 0, trunFlags, U32((uint)samples.Length), U32((uint)dataOffset), entries)));
        var moof = Build(0);
        moof = Build(moof.Length + 8);
        var data = samples.SelectMany((s, i) => Enumerable.Repeat((byte)(track * 16 + firstSample + i), (int)s.Size)).ToArray();
        return [.. moof, .. Box("mdat", data)];
    }

    private string WriteFragmented(params byte[][] parts)
    {
        var path = Path.Combine(_dir, "fragmented.mp4");
        File.WriteAllBytes(path, parts.SelectMany(p => p).ToArray());
        return path;
    }

    private string Convert(string source)
    {
        var dest = Path.Combine(_dir, "out.mp4");
        Mp4Defragmenter.Defragment(source, dest);
        return dest;
    }

    // ---- 変換結果を読む ----

    private static IEnumerable<(string Type, int Start, int DataStart, int End)> Children(byte[] d, int start, int end)
    {
        for (var pos = start; pos + 8 <= end;)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(pos));
            var header = 8;
            if (size == 1) { size = (long)BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan(pos + 8)); header = 16; }
            yield return (Encoding.ASCII.GetString(d, pos + 4, 4), pos, pos + header, pos + (int)size);
            pos += (int)size;
        }
    }

    private static (string Type, int Start, int DataStart, int End) Find(byte[] d, params string[] path)
    {
        var (start, end) = (0, d.Length);
        (string, int, int, int) box = default;
        foreach (var type in path)
        {
            box = Children(d, start, end).Single(b => b.Type == type);
            (start, end) = (box.Item3, box.Item4);
        }
        return box;
    }

    private static (string, int, int, int) Track(byte[] d, int index)
    {
        var moov = Find(d, "moov");
        var trak = Children(d, moov.DataStart, moov.End).Where(b => b.Type == "trak").ElementAt(index);
        return trak;
    }

    private static (string, int, int, int) InTrack(byte[] d, int index, params string[] path)
    {
        var (_, _, start, end) = Track(d, index);
        (string, int, int, int) box = default;
        foreach (var type in path)
        {
            box = Children(d, start, end).Single(b => b.Type == type);
            (start, end) = (box.Item3, box.Item4);
        }
        return box;
    }

    private static uint ReadU32(byte[] d, int pos) => BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(pos));

    private static uint[] Table(byte[] d, (string, int, int DataStart, int) box, int skip, int entrySize, int field = 0)
    {
        var count = (int)ReadU32(d, box.DataStart + 4 + skip);
        return Enumerable.Range(0, count).Select(i => ReadU32(d, box.DataStart + 8 + skip + i * entrySize + field)).ToArray();
    }

    // ---- テスト ----

    [Fact]
    public void Defragment_BuildsSampleTablesAndKeepsSampleData()
    {
        var source = WriteFragmented(
            Ftyp, Moov(),
            Fragment(1, 0, [(10, 1000, true), (5, 1000, false), (7, 1001, false)]),
            Fragment(2, 0, [(3, null, true), (4, null, true)]),
            Fragment(1, 3, [(6, 1000, true), (8, 999, false)]),
            Fragment(2, 2, [(2, null, true)]));

        var d = File.ReadAllBytes(Convert(source));

        Assert.Equal(["ftyp", "mdat", "moov"], Children(d, 0, d.Length).Select(b => b.Type));
        var moov = Find(d, "moov");
        Assert.DoesNotContain(Children(d, moov.DataStart, moov.End), b => b.Type == "mvex");
        Assert.Equal("mvhd", Children(d, moov.DataStart, moov.End).First().Type);

        // 映像: 5 サンプル、2 塊、キーフレームは 1 と 4
        var stts = InTrack(d, 0, "mdia", "minf", "stbl", "stts");
        Assert.Equal([2u, 1u, 1u, 1u], Table(d, stts, 0, 8));
        Assert.Equal([1000u, 1001u, 1000u, 999u], Table(d, stts, 0, 8, 4));
        Assert.Equal([10u, 5u, 7u, 6u, 8u], Table(d, InTrack(d, 0, "mdia", "minf", "stbl", "stsz"), 4, 4));
        Assert.Equal([1u, 4u], Table(d, InTrack(d, 0, "mdia", "minf", "stbl", "stss"), 0, 4));
        var stsc = InTrack(d, 0, "mdia", "minf", "stbl", "stsc");
        Assert.Equal([1u, 2u], Table(d, stsc, 0, 12));
        Assert.Equal([3u, 2u], Table(d, stsc, 0, 12, 4));

        // 音声: trex の既定の長さ 1024 を使い、すべてキーフレームなので stss はない
        Assert.Equal([3u], Table(d, InTrack(d, 1, "mdia", "minf", "stbl", "stts"), 0, 8));
        Assert.Equal([1024u], Table(d, InTrack(d, 1, "mdia", "minf", "stbl", "stts"), 0, 8, 4));
        Assert.DoesNotContain(Children(d, InTrack(d, 1, "mdia", "minf", "stbl").Item3, InTrack(d, 1, "mdia", "minf", "stbl").Item4), b => b.Type == "stss");

        // co64 が指す場所に元のサンプルのデータがある
        var videoChunks = InTrack(d, 0, "mdia", "minf", "stbl", "co64");
        var offsets = Enumerable.Range(0, 2).Select(i => (long)BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan(videoChunks.Item3 + 8 + i * 8))).ToArray();
        Assert.Equal((byte)(16 + 0), d[offsets[0]]);
        Assert.Equal((byte)(16 + 1), d[offsets[0] + 10]);
        Assert.Equal((byte)(16 + 3), d[offsets[1]]);
        var audioChunk = InTrack(d, 1, "mdia", "minf", "stbl", "co64");
        Assert.Equal((byte)(32 + 2), d[(long)BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan(audioChunk.Item3 + 8 + 8))]);

        // 長さ: 映像 5000/60000、音声 3072/48000（mvhd は v1、tkhd・mdhd は v0）
        Assert.Equal(5000u, ReadU32(d, InTrack(d, 0, "mdia", "mdhd").Item3 + 16));
        Assert.Equal(5000u, ReadU32(d, InTrack(d, 0, "tkhd").Item3 + 20));
        Assert.Equal(3840u, ReadU32(d, InTrack(d, 1, "tkhd").Item3 + 20));
        var mvhd = Find(d, "moov", "mvhd");
        Assert.Equal(5000UL, BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan(mvhd.DataStart + 24)));
    }

    [Fact]
    public void Defragment_StopsAtTruncatedFragment()
    {
        var complete = Fragment(1, 0, [(10, 1000, true), (5, 1000, false)]);
        var truncated = Fragment(1, 2, [(10, 1000, true), (5, 1000, false)]);
        var source = WriteFragmented(Ftyp, Moov(), complete, truncated[..^4]);

        var d = File.ReadAllBytes(Convert(source));

        Assert.Equal([10u, 5u], Table(d, InTrack(d, 0, "mdia", "minf", "stbl", "stsz"), 4, 4));
    }

    [Fact]
    public void IsFragmented_DistinguishesFragmentedAndConverted()
    {
        var source = WriteFragmented(Ftyp, Moov(), Fragment(1, 0, [(10, 1000, true)]));

        Assert.True(Mp4Defragmenter.IsFragmented(source));
        Assert.False(Mp4Defragmenter.IsFragmented(Convert(source)));
    }
}
