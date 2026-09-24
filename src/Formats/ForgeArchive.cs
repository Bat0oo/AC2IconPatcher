using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AC2IconPatcher;

/// <summary>Jedan .data fajl unutar forgea, prema indeksu.</summary>
public sealed class ForgeEntry
{
    public int Index;            // redni broj (1-baziran), isti kao u imenima koje AnvilToolkit pravi
    public long HeaderOffset;    // pocetak FILEDATA zaglavlja
    public uint Hash;
    public int Size;             // velicina .data sadrzaja
    public string Name = "";
    public long DataOffset => HeaderOffset + ForgeArchive.EntryHeaderSize;
}

/// <summary>
/// Puno citanje i pisanje .forge arhive preko indeksa.
///
/// Format (utvrdjen poredjenjem DataPC_LGS07_San_Marco.forge sa njegovim
/// otpakovanim sadrzajem, provjereno na 1020 fajlova):
///
///   offset 0      "scimitar\0"
///   offset 9      u32 verzija (25 za AC2)
///   offset 13     u32 offset indeksa (1046)
///   offset 1150   glavna tabela, zapis 16 B:
///                     u64 offset zaglavlja, u32 hash, u32 velicina
///   offset 81298  tabela imena, zapis 188 B:
///                     velicina na +24, ime na +68
///
///   Svaki zapis: 440 B zaglavlje koje pocinje sa "FILEDATA" (velicina se
///   ponavlja na +395), pa sadrzaj .data fajla. Zapisi su poravnati na 2048 B.
/// </summary>
public sealed class ForgeArchive
{
    public const int MainTableOffset = 1150;
    public const int MainRecordSize = 16;
    public const int NameRecordSize = 188;
    public const int NameSizeField = 24;
    public const int NameField = 68;
    public const int EntryHeaderSize = 440;
    public const int HeaderSizeField = 395;
    public const int Alignment = 2048;

    public byte[] Raw = Array.Empty<byte>();
    public List<ForgeEntry> Entries = new();
    public int Version;
    public int NameTableOffset = -1;   // razlikuje se od forgea do forgea, pa ga trazimo

    public static ForgeArchive Read(string path)
    {
        var a = new ForgeArchive { Raw = File.ReadAllBytes(path) };
        var buf = a.Raw;

        if (Encoding.ASCII.GetString(buf, 0, 8) != "scimitar")
            throw new InvalidDataException("Nije forge fajl.");
        a.Version = BitConverter.ToInt32(buf, 9);
        if (a.Version != 25)
            throw new NotSupportedException($"Forge verzija {a.Version} nije podrzana (ocekujem 25 za AC2).");

        for (int i = 0; ; i++)
        {
            int rec = MainTableOffset + MainRecordSize * i;
            if (rec + MainRecordSize > buf.Length) break;

            long off = (long)BitConverter.ToUInt64(buf, rec);
            uint hash = BitConverter.ToUInt32(buf, rec + 8);
            int size = BitConverter.ToInt32(buf, rec + 12);

            if (off <= 0 || size <= 0 || off + EntryHeaderSize + size > buf.Length) break;
            if (Encoding.ASCII.GetString(buf, (int)off, 8) != "FILEDATA") break;

            a.Entries.Add(new ForgeEntry
            {
                Index = i + 1,
                HeaderOffset = off,
                Hash = hash,
                Size = size
            });
        }

        a.NameTableOffset = a.FindNameTable();
        if (a.NameTableOffset >= 0)
            foreach (var e in a.Entries)
            {
                int nrec = a.NameTableOffset + NameRecordSize * (e.Index - 1);
                if (nrec + NameRecordSize > buf.Length) break;
                int start = nrec + NameField, end = start;
                while (end < buf.Length && buf[end] != 0) end++;
                e.Name = Encoding.ASCII.GetString(buf, start, end - start);
            }

        return a;
    }

    /// <summary>
    /// Tabela imena nije na istoj adresi u svakom forgeu, pa je trazimo: to je
    /// mjesto na kojem se, sa korakom od 188 bajta, ponavljaju tacno one velicine
    /// koje pise glavna tabela.
    /// </summary>
    private int FindNameTable()
    {
        if (Entries.Count < 3) return -1;
        int probe = Math.Min(8, Entries.Count);
        int limit = (int)Math.Min(Raw.Length - NameRecordSize * (long)probe, 2_000_000);

        for (int p = MainTableOffset; p < limit; p += 4)
        {
            bool all = true;
            for (int i = 0; i < probe; i++)
            {
                int at = p + NameRecordSize * i + NameSizeField;
                if (at + 4 > Raw.Length || BitConverter.ToInt32(Raw, at) != Entries[i].Size) { all = false; break; }
            }
            if (all) return p;
        }
        return -1;
    }

    public byte[] GetData(ForgeEntry e) => Raw[(int)e.DataOffset..(int)(e.DataOffset + e.Size)];

    /// <summary>
    /// Snima forge sa izmijenjenim sadrzajem odredjenih zapisa.
    ///
    /// Zapisi u forgeu NISU poredani po offsetu - zapis 9 zna biti na 166 MB, a
    /// zapis 11 na 1.8 MB. Zato se ne smije prepisivati cijeli fajl redom.
    /// Umjesto toga sve ostaje tacno gdje jeste, a izmijenjeni zapis se upisuje
    /// na svoje mjesto ako nova velicina stane u prostor do sljedeceg zapisa;
    /// ako ne stane, dopisuje se na kraj fajla i samo mu se offset azurira.
    /// </summary>
    public void Write(string outPath, Dictionary<int, byte[]> replacements)
    {
        var ms = new MemoryStream();
        ms.Write(Raw, 0, Raw.Length);

        // granice: koliko prostora svaki zapis ima do sljedeceg po redoslijedu u fajlu
        var byOffset = new List<ForgeEntry>(Entries);
        byOffset.Sort((x, y) => x.HeaderOffset.CompareTo(y.HeaderOffset));

        foreach (var kv in replacements)
        {
            var e = Entries.Find(x => x.Index == kv.Key)
                    ?? throw new InvalidDataException($"Nema zapisa {kv.Key}");
            var data = kv.Value;

            int pos = byOffset.IndexOf(e);
            long nextStart = pos + 1 < byOffset.Count ? byOffset[pos + 1].HeaderOffset : Raw.Length;
            long available = nextStart - e.HeaderOffset;
            long needed = EntryHeaderSize + data.Length;

            long newOffset;
            if (needed <= available)
            {
                newOffset = e.HeaderOffset;              // stane na svoje mjesto
            }
            else
            {
                long end = ms.Length;
                newOffset = end + (Alignment - (end % Alignment)) % Alignment;
                ms.Position = ms.Length;
                for (long i = ms.Length; i < newOffset; i++) ms.WriteByte(0);
            }

            var header = Raw[(int)e.HeaderOffset..(int)(e.HeaderOffset + EntryHeaderSize)];
            BitConverter.GetBytes(data.Length).CopyTo(header, HeaderSizeField);

            ms.Position = newOffset;
            ms.Write(header, 0, header.Length);
            ms.Write(data, 0, data.Length);

            // ostatak starog prostora nulirati da ne ostanu smece bajtovi
            if (newOffset == e.HeaderOffset)
                for (long i = needed; i < available; i++) ms.WriteByte(0);

            var buf = ms.GetBuffer();
            int rec = MainTableOffset + MainRecordSize * (e.Index - 1);
            BitConverter.GetBytes((ulong)newOffset).CopyTo(buf, rec);
            BitConverter.GetBytes(data.Length).CopyTo(buf, rec + 12);

            if (NameTableOffset >= 0)
            {
                int nrec = NameTableOffset + NameRecordSize * (e.Index - 1) + NameSizeField;
                if (nrec + 4 <= buf.Length) BitConverter.GetBytes(data.Length).CopyTo(buf, nrec);
            }

            Console.WriteLine($"  zapis {e.Index}: {e.Size:N0} -> {data.Length:N0} B, " +
                              (newOffset == e.HeaderOffset ? "na istom mjestu" : $"premjesten na {newOffset:N0}"));
        }

        File.WriteAllBytes(outPath, ms.ToArray());
    }
}