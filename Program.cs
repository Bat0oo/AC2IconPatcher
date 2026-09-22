using System;
using System.IO;
using System.Linq;
using AC2IconPatcher;

if (args.Length == 0) { Usage(); return 1; }

try
{
    switch (args[0].ToLower())
    {
        case "info": return CmdInfo(args);
        case "export": return CmdExport(args);
        case "selftest": return CmdSelfTest(args);
        case "forge-info": return CmdForgeInfo(args);
        case "forge-export": return CmdForgeExport(args);
        default: Usage(); return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine("GRESKA: " + ex.Message);
    return 1;
}

static void Usage()
{
    Console.WriteLine("""
    AC2IconPatcher - alat za HUD ikonice u Assassin's Creed 2

      info <fajl.data>
          Ispisuje blokove, chunkove, provjeru checksuma i pronadjene teksture.

      export <fajl.data> <izlazni-folder>
          Izvlaci sve teksture iz .data fajla kao PNG.

      forge-info <fajl.forge>
          Skenira cijeli .forge i ispisuje pronadjene teksture.

      forge-export <fajl.forge> <izlazni-folder>
          Izvlaci sve teksture iz .forge fajla kao PNG.

      selftest <folder-sa-data-fajlovima>
          Prolazi kroz sve .data fajlove u folderu, dekompresuje ih i provjerava
          checksum svakog chunka. Ovo je test da li dekoder radi ispravno.
    """);
}

static int CmdInfo(string[] args)
{
    if (args.Length < 2) { Usage(); return 1; }
    var df = DataFile.ReadFile(args[1]);

    Console.WriteLine($"Blokova: {df.Blocks.Count}");
    int ci = 0;
    foreach (var (blk, i) in df.Blocks.Select((b, i) => (b, i)))
    {
        int stored = blk.Chunks.Count(c => c.IsStored);
        Console.WriteLine($"  blok {i}: verzija={blk.Version} kompresija={blk.CompressionType} " +
                          $"maxChunk={blk.MaxChunkSize} chunkova={blk.Chunks.Count} (sirovih: {stored})");
        ci += blk.Chunks.Count;
    }

    var (ok, bad) = df.VerifyChecksums();
    Console.WriteLine($"Checksum: {ok} ispravnih, {bad} neispravnih");

    var content = df.GetContent();
    Console.WriteLine($"Dekompresovano: {content.Length:N0} bajtova");

    var textures = DataFile.FindEntries(content, DataEntry.TypeTextureMap);
    Console.WriteLine($"\nTeksture ({textures.Count}):");
    foreach (var e in textures)
    {
        var tm = new TextureMap(content, e.Offset);
        string fmt = tm.Format switch
        {
            TextureMap.FormatRgba => "RGBA",
            TextureMap.FormatDxt5 => "DXT5",
            _ => $"format {tm.Format}"
        };
        Console.WriteLine($"  {e.Id,8}  {e.Name,-40} {tm.Width}x{tm.Height} {fmt} {tm.DataSize:N0} B");
    }
    return 0;
}

static int CmdExport(string[] args)
{
    if (args.Length < 3) { Usage(); return 1; }
    Directory.CreateDirectory(args[2]);

    var content = DataFile.ReadFile(args[1]).GetContent();
    var textures = DataFile.FindEntries(content, DataEntry.TypeTextureMap);

    int n = 0;
    foreach (var e in textures)
    {
        var tm = new TextureMap(content, e.Offset);
        if (!tm.LooksValid) continue;
        try
        {
            var rgba = tm.DecodeTopMipRgba();
            string safe = string.Join("_", e.Name.Split(Path.GetInvalidFileNameChars()));
            string outPath = Path.Combine(args[2], $"{e.Id}_{safe}.png");
            Png.WriteRgba(outPath, rgba, tm.Width, tm.Height);
            Console.WriteLine($"  {outPath}  ({tm.Width}x{tm.Height})");
            n++;
        }
        catch (NotSupportedException ex) { Console.WriteLine($"  preskocen {e.Name}: {ex.Message}"); }
    }
    Console.WriteLine($"\nIzvezeno: {n}");
    return 0;
}

static int CmdSelfTest(string[] args)
{
    if (args.Length < 2) { Usage(); return 1; }
    var files = Directory.GetFiles(args[1], "*.data").OrderBy(x => x).ToArray();
    if (files.Length == 0) { Console.WriteLine("Nema .data fajlova u tom folderu."); return 1; }

    int pass = 0, fail = 0;
    foreach (var f in files)
    {
        try
        {
            var df = DataFile.ReadFile(f);
            var (ok, bad) = df.VerifyChecksums();
            var content = df.GetContent();
            bool good = bad == 0 && content.Length > 0;
            Console.WriteLine($"{(good ? "OK  " : "FAIL")} {Path.GetFileName(f),-50} " +
                              $"{content.Length,12:N0} B  chunkova OK: {ok}, lose: {bad}");
            if (good) pass++; else fail++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL {Path.GetFileName(f)}: {ex.Message}");
            fail++;
        }
    }
    Console.WriteLine($"\n{pass} prosao, {fail} pao");
    return fail > 0 ? 1 : 0;
}


static byte[] LoadForge(string path)
{
    Console.WriteLine($"Citam {Path.GetFileName(path)} ...");
    var content = ForgeFile.ReadAllContent(path, (blocks, bytes) =>
        Console.Write($"\r  blokova: {blocks,6}  dekompresovano: {bytes / 1024 / 1024,6:N0} MB"));
    Console.WriteLine();
    return content;
}

static int CmdForgeInfo(string[] args)
{
    if (args.Length < 2) { Usage(); return 1; }
    var buf = File.ReadAllBytes(args[1]);
    var (version, indexOffset) = ForgeFile.ReadHeader(buf);
    Console.WriteLine($"Forge verzija: {version}, indeks na offsetu: {indexOffset}");

    var content = LoadForge(args[1]);
    var textures = DataFile.FindEntries(content, DataEntry.TypeTextureMap);
    Console.WriteLine($"\nTeksture ({textures.Count}):");
    foreach (var e in textures)
    {
        var tm = new TextureMap(content, e.Offset);
        if (!tm.LooksValid) continue;
        string fmt = tm.Format switch
        {
            TextureMap.FormatRgba => "RGBA",
            TextureMap.FormatDxt5 => "DXT5",
            _ => $"format {tm.Format}"
        };
        Console.WriteLine($"  {e.Id,8}  {e.Name,-45} {tm.Width}x{tm.Height} {fmt} {tm.DataSize:N0} B");
    }
    return 0;
}

static int CmdForgeExport(string[] args)
{
    if (args.Length < 3) { Usage(); return 1; }
    Directory.CreateDirectory(args[2]);
    var content = LoadForge(args[1]);
    var textures = DataFile.FindEntries(content, DataEntry.TypeTextureMap);

    int n = 0, skipped = 0;
    foreach (var e in textures)
    {
        var tm = new TextureMap(content, e.Offset);
        if (!tm.LooksValid) { skipped++; continue; }
        try
        {
            var rgba = tm.DecodeTopMipRgba();
            string safe = string.Join("_", e.Name.Split(Path.GetInvalidFileNameChars()));
            Png.WriteRgba(Path.Combine(args[2], $"{e.Id}_{safe}.png"), rgba, tm.Width, tm.Height);
            n++;
        }
        catch (NotSupportedException) { skipped++; }
    }
    Console.WriteLine($"Izvezeno: {n}, preskoceno (nepodrzan format): {skipped}");
    return 0;
}