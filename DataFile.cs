using System;
using System.Collections.Generic;
using System.IO;

namespace AC2IconPatcher;

/// <summary>Jedan fajl unutar dekompresovanog .data streama.</summary>
public record DataEntry(uint TypeHash, uint Id, string Name, int Offset)
{
    public const uint TypeTextureMap = 0xA2B7E917;
    public const uint TypeTextureMapDesc = 0x989DC6B2;
}

/// <summary>Jedan chunk unutar bloka - cuvamo i original da ga mozemo prepisati netaknut.</summary>
public sealed class Chunk
{
    public int UncompressedSize;
    public int CompressedSize;
    public uint Checksum;
    public byte[] RawCompressed = Array.Empty<byte>();  // bajtovi kako stoje u fajlu
    public byte[]? Decompressed;                         // popunjeno tek po potrebi
    public bool IsStored => UncompressedSize == CompressedSize;
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

    public byte[] Header = Array.Empty<byte>();   // 4 bajta prije prvog bloka
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
            throw new InvalidDataException("Nije pronadjen nijedan validan blok - je li ovo .data fajl?");

        return df;
    }

    public static DataFile ReadFile(string path) => Read(File.ReadAllBytes(path));

    /// <summary>Dekompresuje sve chunkove i vraca spojeni sadrzaj.</summary>
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
    /// Adler-32 sa pocetnom vrijednoscu 0 umjesto standardne 1 - tako Anvil racuna
    /// checksum nad kompresovanim bajtovima chunka.
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

    /// <summary>Provjerava da li se checksum svakog chunka poklapa - dobar test da citamo format ispravno.</summary>
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

    /// <summary>Pronalazi fajlove u dekompresovanom sadrzaju po type hashu.</summary>
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
