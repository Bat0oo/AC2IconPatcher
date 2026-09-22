// Lzo2a.cs -- LZO2A decompression, ported to C#
//
// Ported from lzo2a_d.ch / config2a.h of the LZO real-time data compression
// library, Copyright (C) 1996-2017 Markus Franz Xaver Johannes Oberhumer.
// LZO is licensed under the GNU General Public License v2 or later, therefore
// this file - and any program linking it - is GPL v2+ as well.
//
// Original: http://www.oberhumer.com/opensource/lzo/
//
// Assassin's Creed 2 .data containers compress their chunks with LZO2A, which
// is NOT the same as the much more common LZO1X (lzo.net and most other
// managed ports only implement LZO1X and will fail on this data).
//
// Note on the configuration: LZO ships config2a.h with SWD_N = 8191, which
// means the "#if (SWD_N >= 8192)" M3-match branch is compiled out. That branch
// is therefore intentionally absent below.

using System;
using System.IO;

namespace AC2IconPatcher;

public static class Lzo2a
{
    private const int M1_MIN_LEN = 2;

    /// <summary>
    /// Decompresses an LZO2A block. <paramref name="outLen"/> must be the exact
    /// uncompressed size (the .data chunk table stores it).
    /// </summary>
    public static byte[] Decompress(ReadOnlySpan<byte> src, int outLen)
    {
        var dst = new byte[outLen];
        int ip = 0, op = 0;
        uint b = 0;      // bit buffer
        int k = 0;       // number of valid bits in b

        while (op < outLen)
        {
            // --- literal? ---
            if (k < 1) { b |= (uint)src[ip++] << k; k += 8; }
            if ((b & 1) == 0)
            {
                b >>= 1; k -= 1;
                dst[op++] = src[ip++];
                continue;
            }
            b >>= 1; k -= 1;

            // --- M1 match? ---
            if (k < 1) { b |= (uint)src[ip++] << k; k += 8; }
            if ((b & 1) == 0)
            {
                b >>= 1; k -= 1;

                if (k < 2) { b |= (uint)src[ip++] << k; k += 8; }
                int t1 = M1_MIN_LEN + (int)(b & 3);
                b >>= 2; k -= 2;

                int m1 = op - 1 - src[ip++];
                if (m1 < 0) throw new InvalidDataException("LZO2A: lookbehind overrun");
                CopyOverlapping(dst, m1, ref op, t1, outLen);
                continue;
            }
            b >>= 1; k -= 1;

            // --- M2 / long match ---
            int t = src[ip++];
            int mpos = op;
            mpos -= (t & 31) | (src[ip++] << 5);
            t >>= 5;

            if (t == 0)
            {
                t = 10 - 1;
                while (src[ip] == 0) { t += 255; ip++; }
                t += src[ip++];
            }
            else
            {
                if (mpos == op) break;   // LZO_EOF_CODE: end of block
                t += 2;
            }

            if (mpos < 0) throw new InvalidDataException("LZO2A: lookbehind overrun");
            CopyOverlapping(dst, mpos, ref op, t, outLen);
        }

        if (op != outLen)
            throw new InvalidDataException($"LZO2A: produced {op} bytes, expected {outLen}");

        return dst;
    }

    // LZO matches may overlap the current output position (run-length style),
    // so this has to copy byte by byte - Array.Copy would be wrong here.
    private static void CopyOverlapping(byte[] dst, int from, ref int op, int len, int outLen)
    {
        if (op + len > outLen) throw new InvalidDataException("LZO2A: output overrun");
        for (int i = 0; i < len; i++) dst[op + i] = dst[from + i];
        op += len;
    }
}
