using System;
using System.IO;

namespace AC2IconPatcher;

/// <summary>
/// TextureMap header, as it sits in the .data stream (the same thing AnvilToolkit
/// extracts as a separate .TextureMap file).
///
/// Offsets were determined by comparing real files from the game:
///   10  u32 width
///   14  u32 height
///   22  u32 format   (0 = raw RGBA8888, 5 = DXT5)
///   82  u32 data size (includes the mip chain)
///   86      pixels
/// The image is stored vertically flipped - the bottom row comes first.
/// </summary>
public sealed class TextureMap
{
    public const int OffWidth = 10, OffHeight = 14, OffFormat = 22, OffDataSize = 82, OffPixels = 86;

    public const uint FormatRgba = 0;
    public const uint FormatDxt5 = 5;

    public int Width, Height;
    public uint Format;
    public int DataSize;
    public int PixelStart;          // absolute offset in the buffer
    private readonly byte[] _buf;

    public TextureMap(byte[] buf, int offset)
    {
        _buf = buf;
        Width = BitConverter.ToInt32(buf, offset + OffWidth);
        Height = BitConverter.ToInt32(buf, offset + OffHeight);
        Format = BitConverter.ToUInt32(buf, offset + OffFormat);
        DataSize = BitConverter.ToInt32(buf, offset + OffDataSize);
        PixelStart = offset + OffPixels;
    }

    public bool LooksValid =>
        Width > 0 && Width <= 4096 && Height > 0 && Height <= 4096
        && DataSize > 0 && PixelStart + DataSize <= _buf.Length;

    /// <summary>Returns the largest mip as RGBA, already flipped into normal orientation.</summary>
    public byte[] DecodeTopMipRgba()
    {
        byte[] rgba = Format switch
        {
            FormatRgba => _buf[PixelStart..(PixelStart + Width * Height * 4)],
            FormatDxt5 => DecodeDxt5(_buf.AsSpan(PixelStart, Math.Max(16, Width * Height)), Width, Height),
            _ => throw new NotSupportedException($"Format {Format} is not supported yet")
        };
        return FlipVertically(rgba, Width, Height);
    }

    public static byte[] FlipVertically(byte[] rgba, int w, int h)
    {
        var outp = new byte[rgba.Length];
        int stride = w * 4;
        for (int y = 0; y < h; y++)
            Array.Copy(rgba, y * stride, outp, (h - 1 - y) * stride, stride);
        return outp;
    }

    private static byte[] DecodeDxt5(ReadOnlySpan<byte> src, int w, int h)
    {
        var dst = new byte[w * h * 4];
        int bi = 0;
        for (int by = 0; by < h; by += 4)
            for (int bx = 0; bx < w; bx += 4, bi += 16)
            {
                var blk = src.Slice(bi, 16);

                // alpha
                byte a0 = blk[0], a1 = blk[1];
                var alpha = new byte[8];
                alpha[0] = a0; alpha[1] = a1;
                if (a0 > a1)
                    for (int i = 0; i < 6; i++) alpha[i + 2] = (byte)(((6 - i) * a0 + (1 + i) * a1) / 7);
                else
                {
                    for (int i = 0; i < 4; i++) alpha[i + 2] = (byte)(((4 - i) * a0 + (1 + i) * a1) / 5);
                    alpha[6] = 0; alpha[7] = 255;
                }
                ulong abits = 0;
                for (int i = 0; i < 6; i++) abits |= (ulong)blk[2 + i] << (8 * i);

                // color
                ushort c0 = BitConverter.ToUInt16(blk[8..]), c1 = BitConverter.ToUInt16(blk[10..]);
                var col = new (byte r, byte g, byte b)[4];
                col[0] = Rgb565(c0); col[1] = Rgb565(c1);
                col[2] = (Lerp(col[0].r, col[1].r, 2, 1), Lerp(col[0].g, col[1].g, 2, 1), Lerp(col[0].b, col[1].b, 2, 1));
                col[3] = (Lerp(col[0].r, col[1].r, 1, 2), Lerp(col[0].g, col[1].g, 1, 2), Lerp(col[0].b, col[1].b, 1, 2));
                uint cbits = BitConverter.ToUInt32(blk[12..]);

                for (int py = 0; py < 4; py++)
                    for (int px = 0; px < 4; px++)
                    {
                        int x = bx + px, y = by + py;
                        if (x >= w || y >= h) continue;
                        int idx = py * 4 + px;
                        var c = col[(int)((cbits >> (2 * idx)) & 3)];
                        byte a = alpha[(int)((abits >> (3 * idx)) & 7)];
                        int o = (y * w + x) * 4;
                        dst[o] = c.r; dst[o + 1] = c.g; dst[o + 2] = c.b; dst[o + 3] = a;
                    }
            }
        return dst;
    }

    /// <summary>
    /// Builds a complete RGBA block with mips, ready to be written in place of
    /// the original. Input is the image in normal orientation - we do the flip.
    /// Only works for format 0 (raw RGBA); DXT would need an encoder.
    /// </summary>
    public byte[] EncodeRgbaWithMips(byte[] rgba)
    {
        if (Format != FormatRgba)
            throw new NotSupportedException($"Writing is only supported for RGBA (format 0), this texture is format {Format}");
        if (rgba.Length != Width * Height * 4)
            throw new ArgumentException($"Expected {Width}x{Height} RGBA ({Width * Height * 4} B), got {rgba.Length} B");

        var outp = new byte[DataSize];
        var level = FlipVertically(rgba, Width, Height);
        int w = Width, h = Height, pos = 0;

        while (pos < DataSize && w >= 1 && h >= 1)
        {
            int n = Math.Min(level.Length, DataSize - pos);
            Array.Copy(level, 0, outp, pos, n);
            pos += level.Length;
            if (w == 1 && h == 1) break;
            level = Downsample(level, w, h);
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }
        return outp;
    }

    /// <summary>Simple box downsample by half - good enough for icons.</summary>
    private static byte[] Downsample(byte[] src, int w, int h)
    {
        int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2);
        var dst = new byte[nw * nh * 4];
        for (int y = 0; y < nh; y++)
            for (int x = 0; x < nw; x++)
                for (int c = 0; c < 4; c++)
                {
                    int x0 = Math.Min(w - 1, x * 2), x1 = Math.Min(w - 1, x * 2 + 1);
                    int y0 = Math.Min(h - 1, y * 2), y1 = Math.Min(h - 1, y * 2 + 1);
                    int sum = src[(y0 * w + x0) * 4 + c] + src[(y0 * w + x1) * 4 + c]
                            + src[(y1 * w + x0) * 4 + c] + src[(y1 * w + x1) * 4 + c];
                    dst[(y * nw + x) * 4 + c] = (byte)(sum / 4);
                }
        return dst;
    }

    private static (byte, byte, byte) Rgb565(ushort v) =>
        ((byte)(((v >> 11) & 31) * 255 / 31), (byte)(((v >> 5) & 63) * 255 / 63), (byte)((v & 31) * 255 / 31));

    private static byte Lerp(byte a, byte b, int wa, int wb) => (byte)((a * wa + b * wb) / (wa + wb));
}