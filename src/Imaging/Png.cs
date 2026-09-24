using System;
using System.IO;
using System.IO.Compression;

namespace AC2IconPatcher;

/// <summary>
/// A small PNG encoder/decoder, so the project has no external dependency
/// (System.Drawing doesn't work everywhere, ImageSharp would be a NuGet package).
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

        // scanline filter 0 in front of every row
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
            throw new NotSupportedException("Expected an 8-bit RGBA PNG (no palette or interlacing).");

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
        return Unfilter(raw, w, h);
    }

    /// <summary>Reverses PNG scanline filters (types 0-4).</summary>
    private static byte[] Unfilter(byte[] raw, int w, int h)
    {
        int stride = w * 4, bpp = 4;
        var rgba = new byte[stride * h];

        for (int y = 0; y < h; y++)
        {
            int rowStart = y * (stride + 1);
            byte filter = raw[rowStart];
            int outRow = y * stride;

            for (int x = 0; x < stride; x++)
            {
                byte cur = raw[rowStart + 1 + x];
                byte a = x >= bpp ? rgba[outRow + x - bpp] : (byte)0;              // left
                byte b = y > 0 ? rgba[outRow - stride + x] : (byte)0;              // above
                byte c = (x >= bpp && y > 0) ? rgba[outRow - stride + x - bpp] : (byte)0;  // above-left

                int val = filter switch
                {
                    0 => cur,
                    1 => cur + a,
                    2 => cur + b,
                    3 => cur + (a + b) / 2,
                    4 => cur + Paeth(a, b, c),
                    _ => throw new NotSupportedException($"Unknown PNG filter {filter}")
                };
                rgba[outRow + x] = (byte)val;
            }
        }
        return rgba;
    }

    private static byte Paeth(byte a, byte b, byte c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
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