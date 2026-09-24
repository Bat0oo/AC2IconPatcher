using System;
using System.Collections.Generic;
using System.IO;

namespace AC2IconPatcher;

/// <summary>One file inside the decompressed .data stream.</summary>
public record DataEntry(uint TypeHash, uint Id, string Name, int Offset)
{
    public const uint TypeTextureMap = 0xA2B7E917;
    public const uint TypeTextureMapDesc = 0x989DC6B2;
}

/// <summary>One chunk inside a block - we keep the original too so it can be written back untouched.</summary>
public sealed class Chunk
{
    public int UncompressedSize;
    public int CompressedSize;
    public uint Checksum;
    public byte[] RawCompressed = Array.Empty<byte>();  // bytes as they sit in the file
    public byte[]? Decompressed;                         // filled in only when needed
    public bool IsStored => UncompressedSize == CompressedSize;
    public bool Dirty;                                   // modified, gets written raw
}

public sealed class Block
{
    public ushort Version;
    public byte CompressionType;
    public ushort MaxChunkSize;
    public ushort Unknown;
    public List<Chunk> Chunks = new();
}

public sealed class DataFile
{
    public const ulong Magic = 0x1004FA9957FBAA33;

    public byte[] Header = Array.Empty<byte>();   // 4 bytes before the first block
    public List<Block> Blocks = new();

    public static DataFile Read(byte[] buf, int start = 0, int? limit = null)
    {
        int end = limit ?? buf.Length;
        var df = new DataFile { Header = buf[start..(start + 4)] };
        int pos = start + 4;

        while (pos < end - 8 && BitConverter.ToUInt64(buf, pos) == Magic)
        {
            var blk = new Block();
            pos += 8;
            blk.Version = BitConverter.ToUInt16(buf, pos); pos += 2;
            blk.CompressionType = buf[pos]; pos += 1;
            blk.MaxChunkSize = BitConverter.ToUInt16(buf, pos); pos += 2;
            blk.Unknown = BitConverter.ToUInt16(buf, pos); pos += 2;
            int n = BitConverter.ToUInt16(buf, pos); pos += 2;

            var sizes = new (int u, int c)[n];
            for (int i = 0; i < n; i++)
            {
                sizes[i] = (BitConverter.ToUInt16(buf, pos), BitConverter.ToUInt16(buf, pos + 2));
                pos += 4;
            }

            foreach (var (u, c) in sizes)
            {
                var ch = new Chunk
                {
                    UncompressedSize = u,
                    CompressedSize = c,
                    Checksum = BitConverter.ToUInt32(buf, pos)
                };
                pos += 4;
                ch.RawCompressed = buf[pos..(pos + c)];
                pos += c;
                blk.Chunks.Add(ch);
            }
            df.Blocks.Add(blk);
        }

        if (df.Blocks.Count == 0)
            throw new InvalidDataException("No valid block found - is this a .data file?");

        return df;
    }

    public static DataFile ReadFile(string path) => Read(File.ReadAllBytes(path));

    /// <summary>Global offset of the start of each chunk in the decompressed content.</summary>
    public List<(Chunk chunk, int start, int length)> ChunkMap()
    {
        var map = new List<(Chunk, int, int)>();
        int pos = 0;
        foreach (var blk in Blocks)
            foreach (var ch in blk.Chunks)
            {
                map.Add((ch, pos, ch.UncompressedSize));
                pos += ch.UncompressedSize;
            }
        return map;
    }

    /// <summary>
    /// Writes bytes at the given position in the decompressed content. Only touches
    /// the chunks the change actually hits - those get written raw when saved,
    /// everything else is copied through byte for byte unchanged.
    /// </summary>
    public void PatchBytes(int globalOffset, ReadOnlySpan<byte> data)
    {
        foreach (var (ch, start, len) in ChunkMap())
        {
            int end = start + len;
            if (globalOffset + data.Length <= start || globalOffset >= end) continue;

            ch.Decompressed ??= ch.IsStored ? ch.RawCompressed
                                            : Lzo2a.Decompress(ch.RawCompressed, ch.UncompressedSize);
            var buf = (byte[])ch.Decompressed.Clone();

            int from = Math.Max(globalOffset, start);
            int to = Math.Min(globalOffset + data.Length, end);
            for (int i = from; i < to; i++)
                buf[i - start] = data[i - globalOffset];

            ch.Decompressed = buf;
            ch.Dirty = true;
        }
    }

    /// <summary>Serializes the .data back. Modified chunks are written uncompressed.</summary>
    public byte[] Serialize()
    {
        var ms = new MemoryStream();
        ms.Write(Header, 0, Header.Length);

        foreach (var blk in Blocks)
        {
            ms.Write(BitConverter.GetBytes(Magic));
            ms.Write(BitConverter.GetBytes(blk.Version));
            ms.WriteByte(blk.CompressionType);
            ms.Write(BitConverter.GetBytes(blk.MaxChunkSize));
            ms.Write(BitConverter.GetBytes(blk.Unknown));
            ms.Write(BitConverter.GetBytes((ushort)blk.Chunks.Count));

            var payloads = new byte[blk.Chunks.Count][];
            for (int i = 0; i < blk.Chunks.Count; i++)
            {
                var ch = blk.Chunks[i];
                // A raw chunk (u == c) is something the game supports - real files have
                // them too - so we don't need a compressor at all.
                payloads[i] = ch.Dirty ? ch.Decompressed! : ch.RawCompressed;
                ms.Write(BitConverter.GetBytes((ushort)ch.UncompressedSize));
                ms.Write(BitConverter.GetBytes((ushort)payloads[i].Length));
            }

            for (int i = 0; i < blk.Chunks.Count; i++)
            {
                var ch = blk.Chunks[i];
                uint sum = ch.Dirty ? Adler32Zero(payloads[i]) : ch.Checksum;
                ms.Write(BitConverter.GetBytes(sum));
                ms.Write(payloads[i], 0, payloads[i].Length);
            }
        }
        return ms.ToArray();
    }

    /// <summary>Decompresses all chunks and returns the concatenated content.</summary>
    public byte[] GetContent()
    {
        var ms = new MemoryStream();
        foreach (var blk in Blocks)
            foreach (var ch in blk.Chunks)
            {
                ch.Decompressed ??= ch.IsStored
                    ? ch.RawCompressed
                    : Lzo2a.Decompress(ch.RawCompressed, ch.UncompressedSize);
                ms.Write(ch.Decompressed, 0, ch.Decompressed.Length);
            }
        return ms.ToArray();
    }

    /// <summary>
    /// Adler-32 with an initial value of 0 instead of the standard 1 - this is how
    /// Anvil computes the checksum over a chunk's compressed bytes.
    /// </summary>
    public static uint Adler32Zero(ReadOnlySpan<byte> data)
    {
        const uint mod = 65521;
        uint a = 0, b = 0;
        foreach (byte x in data)
        {
            a = (a + x) % mod;
            b = (b + a) % mod;
        }
        return (b << 16) | a;
    }

    /// <summary>Checks whether every chunk's checksum matches - a good test that we're reading the format correctly.</summary>
    public (int ok, int bad) VerifyChecksums()
    {
        int ok = 0, bad = 0;
        foreach (var blk in Blocks)
            foreach (var ch in blk.Chunks)
            {
                if (Adler32Zero(ch.RawCompressed) == ch.Checksum) ok++; else bad++;
            }
        return (ok, bad);
    }

    /// <summary>Finds files in the decompressed content by type hash.</summary>
    public static List<DataEntry> FindEntries(byte[] content, uint typeHash)
    {
        var found = new List<DataEntry>();
        var needle = BitConverter.GetBytes(typeHash);

        for (int i = 0; i + 12 < content.Length; i++)
        {
            if (content[i] != needle[0] || content[i + 1] != needle[1]
                || content[i + 2] != needle[2] || content[i + 3] != needle[3]) continue;

            uint id = BitConverter.ToUInt32(content, i + 4);
            int nameLen = BitConverter.ToInt32(content, i + 8);
            if (nameLen <= 0 || nameLen > 200 || i + 12 + nameLen > content.Length) continue;

            bool printable = true;
            for (int k = 0; k < nameLen; k++)
            {
                byte c = content[i + 12 + k];
                if (c < 0x20 || c > 0x7E) { printable = false; break; }
            }
            if (!printable) continue;

            string name = System.Text.Encoding.ASCII.GetString(content, i + 12, nameLen);
            found.Add(new DataEntry(typeHash, id, name, i + 12 + nameLen));
        }
        return found;
    }
}