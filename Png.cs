using System;
using System.IO;
using System.IO.Compression;

namespace AC2IconPatcher;

/// <summary>
/// Mali PNG enkoder/dekoder, da projekat nema nijednu vanjsku zavisnost
/// (System.Drawing ne radi svuda, ImageSharp bi bio NuGet paket).
/// </summary>
public static class Png
{
    public static void WriteRgba(string path, byte[] rgba, int w, int h)
    {
        using var fs = File.Create(path);
        fs.Write(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A });

        var ihdr = new byte[13];
        WriteBe(ihdr, 0, w); WriteBe(ihdr, 4, h);
        ihdr[8] = 8;    // bit depth
        ihdr[9] = 6;    // RGBA
        WriteChunk(fs, "IHDR", ihdr);

        // scanline filter 0 ispred svakog reda
        var raw = new byte[(w * 4 + 1) * h];
        for (int y = 0; y < h; y++)
        {
            raw[y * (w * 4 + 1)] = 0;
            Array.Copy(rgba, y * w * 4, raw, y * (w * 4 + 1) + 1, w * 4);
        }
        WriteChunk(fs, "IDAT", ZlibCompress(raw));
        WriteChunk(fs, "IEND", Array.Empty<byte>());
    }

    public static byte[] ReadRgba(string path, out int w, out int h)
    {
        var b = File.ReadAllBytes(path);
        w = ReadBe(b, 16); h = ReadBe(b, 20);
        if (b[24] != 8 || b[25] != 6)
            throw new NotSupportedException("Ocekujem 8-bitni RGBA PNG (bez paleta i interlacea).");

        var idat = new MemoryStream();
        int pos = 8;
        while (pos < b.Length)
        {
            int len = ReadBe(b, pos);
            string type = System.Text.Encoding.ASCII.GetString(b, pos + 4, 4);
            if (type == "IDAT") idat.Write(b, pos + 8, len);
            pos += 12 + len;
            if (type == "IEND") break;
        }

        var raw = ZlibDecompress(idat.ToArray());
        var rgba = new byte[w * h * 4];
        int stride = w * 4;
        for (int y = 0; y < h; y++)
        {
            byte filter = raw[y * (stride + 1)];
            if (filter != 0) throw new NotSupportedException($"PNG filter {filter} nije podrzan - snimi sliku bez filtera.");
            Array.Copy(raw, y * (stride + 1) + 1, rgba, y * stride, stride);
        }
        return rgba;
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        var ms = new MemoryStream();
        ms.WriteByte(0x78); ms.WriteByte(0x9C);
        using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, true)) ds.Write(data, 0, data.Length);
        uint a = Adler32(data);
        ms.Write(new[] { (byte)(a >> 24), (byte)(a >> 16), (byte)(a >> 8), (byte)a });
        return ms.ToArray();
    }

    private static byte[] ZlibDecompress(byte[] data)
    {
        using var src = new MemoryStream(data, 2, data.Length - 6);
        using var ds = new DeflateStream(src, CompressionMode.Decompress);
        var outMs = new MemoryStream();
        ds.CopyTo(outMs);
        return outMs.ToArray();
    }

    private static uint Adler32(byte[] d)
    {
        uint a = 1, b = 0;
        foreach (byte x in d) { a = (a + x) % 65521; b = (b + a) % 65521; }
        return (b << 16) | a;
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; WriteBe(len, 0, data.Length);
        s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t); s.Write(data);
        var crcBuf = new byte[t.Length + data.Length];
        Array.Copy(t, crcBuf, t.Length); Array.Copy(data, 0, crcBuf, t.Length, data.Length);
        var crc = new byte[4]; WriteBe(crc, 0, (int)Crc32(crcBuf));
        s.Write(crc);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();
    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static uint Crc32(byte[] d)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte x in d) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }

    private static void WriteBe(byte[] b, int o, int v)
    { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    private static int ReadBe(byte[] b, int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
}
