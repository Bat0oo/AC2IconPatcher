using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AC2IconPatcher;

/// <summary>One .data file inside a forge, per the index.</summary>
public sealed class ForgeEntry
{
    public int Index;            // sequence number (1-based), same as in the names AnvilToolkit produces
    public long HeaderOffset;    // start of the FILEDATA header
    public uint Hash;
    public int Size;             // size of the .data content
    public string Name = "";
    public long DataOffset => HeaderOffset + ForgeArchive.EntryHeaderSize;
}

/// <summary>
/// Full read and write of a .forge archive via its index.
///
/// Format (determined by comparing DataPC_LGS07_San_Marco.forge with its
/// unpacked content, verified against 1020 files):
///
///   offset 0      "scimitar\0"
///   offset 9      u32 version (25 for AC2)
///   offset 13     u32 index offset (1046)
///   offset 1150   main table, 16 B record:
///                     u64 header offset, u32 hash, u32 size
///   offset 81298  name table, 188 B record:
///                     size at +24, name at +68
///
///   Each entry: 440 B header starting with "FILEDATA" (size repeats
///   at +395), then the .data file content. Entries are aligned to 2048 B.
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
    public int NameTableOffset = -1;   // differs from forge to forge, so we search for it

    public static ForgeArchive Read(string path)
    {
        var a = new ForgeArchive { Raw = File.ReadAllBytes(path) };
        var buf = a.Raw;

        if (Encoding.ASCII.GetString(buf, 0, 8) != "scimitar")
            throw new InvalidDataException("Not a forge file.");
        a.Version = BitConverter.ToInt32(buf, 9);
        if (a.Version != 25)
            throw new NotSupportedException($"Forge version {a.Version} is not supported (expecting 25 for AC2).");

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
    /// The name table isn't at the same address in every forge, so we search for
    /// it: the place where, at a stride of 188 bytes, exactly the sizes the main
    /// table records repeat.
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
    /// Saves the forge with the modified content of specific entries.
    ///
    /// Entries in a forge are NOT ordered by offset - entry 9 can be at 166 MB
    /// while entry 11 is at 1.8 MB. So the whole file must not be rewritten in
    /// order. Instead everything stays exactly where it is, and a modified entry
    /// is written back to its own spot if the new size fits in the space up to
    /// the next entry; if it doesn't fit, it's appended at the end of the file
    /// and only its offset gets updated.
    /// </summary>
    public void Write(string outPath, Dictionary<int, byte[]> replacements)
    {
        var ms = new MemoryStream();
        ms.Write(Raw, 0, Raw.Length);

        // boundaries: how much space each entry has up to the next one in file order
        var byOffset = new List<ForgeEntry>(Entries);
        byOffset.Sort((x, y) => x.HeaderOffset.CompareTo(y.HeaderOffset));

        foreach (var kv in replacements)
        {
            var e = Entries.Find(x => x.Index == kv.Key)
                    ?? throw new InvalidDataException($"No entry {kv.Key}");
            var data = kv.Value;

            int pos = byOffset.IndexOf(e);
            long nextStart = pos + 1 < byOffset.Count ? byOffset[pos + 1].HeaderOffset : Raw.Length;
            long available = nextStart - e.HeaderOffset;
            long needed = EntryHeaderSize + data.Length;

            long newOffset;
            if (needed <= available)
            {
                newOffset = e.HeaderOffset;              // fits back in its own spot
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

            // zero out the rest of the old space so no garbage bytes are left behind
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

            Console.WriteLine($"  entry {e.Index}: {e.Size:N0} -> {data.Length:N0} B, " +
                              (newOffset == e.HeaderOffset ? "in place" : $"moved to {newOffset:N0}"));
        }

        File.WriteAllBytes(outPath, ms.ToArray());
    }
}