using System;
using System.IO;

namespace AC2IconPatcher;

/// <summary>
/// Citanje .forge kontejnera.
///
/// Indeks forgea jos nije razbijen, ali za citanje nam i ne treba: .data fajlovi
/// lezu nekompresovani unutar forgea, a svaki njihov blok pocinje magicom i sam
/// opisuje svoje chunkove. Zato jednostavno skeniramo cijeli fajl i dekompresujemo
/// sve blokove redom. Granice izmedju pojedinih .data fajlova se ovako gube, ali
/// za pronalazenje i izvoz tekstura to nije bitno.
///
/// Za PISANJE nazad ce indeks biti neophodan - tada se ovo vise nece moci koristiti.
/// </summary>
public static class ForgeFile
{
    public const string Magic = "scimitar";

    public static (int version, long indexOffset) ReadHeader(byte[] buf)
    {
        string magic = System.Text.Encoding.ASCII.GetString(buf, 0, 8);
        if (magic != Magic)
            throw new InvalidDataException($"Nije forge fajl (ocekivao '{Magic}', naso '{magic}')");
        int version = BitConverter.ToInt32(buf, 9);
        long indexOffset = BitConverter.ToUInt32(buf, 13);
        return (version, indexOffset);
    }

    /// <summary>
    /// Dekompresuje sve blokove u forgeu i vraca spojeni sadrzaj.
    /// Pazi: rezultat zna biti nekoliko stotina megabajta.
    /// </summary>
    public static byte[] ReadAllContent(string path, Action<int, long>? progress = null)
    {
        var buf = File.ReadAllBytes(path);
        ReadHeader(buf);

        var ms = new MemoryStream();
        var magicBytes = BitConverter.GetBytes(DataFile.Magic);
        int pos = FindNext(buf, magicBytes, 0);
        int blocks = 0;

        while (pos >= 0 && pos < buf.Length - 17)
        {
            try
            {
                pos += 8;
                pos += 2;  // verzija
                pos += 1;  // tip kompresije
                pos += 4;  // maxChunk + nepoznato
                int n = BitConverter.ToUInt16(buf, pos); pos += 2;

                var sizes = new (int u, int c)[n];
                for (int i = 0; i < n; i++)
                {
                    sizes[i] = (BitConverter.ToUInt16(buf, pos), BitConverter.ToUInt16(buf, pos + 2));
                    pos += 4;
                }

                foreach (var (u, c) in sizes)
                {
                    pos += 4;   // checksum
                    var span = buf.AsSpan(pos, c);
                    var d = (u == c) ? span.ToArray() : Lzo2a.Decompress(span, u);
                    pos += c;
                    ms.Write(d, 0, d.Length);
                }

                blocks++;
                if (progress != null && blocks % 250 == 0) progress(blocks, ms.Length);
            }
            catch
            {
                // Nije bio pravi blok (magic se moze slucajno pojaviti u podacima) -
                // preskoci i trazi sljedeci.
                pos += 8;
            }

            if (pos >= buf.Length - 17) break;
            if (BitConverter.ToUInt64(buf, pos) != DataFile.Magic)
                pos = FindNext(buf, magicBytes, pos);
        }

        progress?.Invoke(blocks, ms.Length);
        return ms.ToArray();
    }

    private static int FindNext(byte[] buf, byte[] needle, int from)
    {
        for (int i = Math.Max(0, from); i <= buf.Length - needle.Length; i++)
        {
            bool hit = true;
            for (int k = 0; k < needle.Length; k++)
                if (buf[i + k] != needle[k]) { hit = false; break; }
            if (hit) return i;
        }
        return -1;
    }
}
