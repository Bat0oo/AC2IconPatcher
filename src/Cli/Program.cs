using System;
using System.IO;
using System.Linq;
using AC2IconPatcher;

// Bez argumenata (npr. dupli klik na exe) pokrecemo interaktivni meni.
if (args.Length == 0)
{
    try { return Wizard.Run(); }
    catch (Exception ex)
    {
        Console.WriteLine("GRESKA: " + ex.Message);
        Console.WriteLine("\nPritisni Enter za izlaz.");
        Console.ReadLine();
        return 1;
    }
}

try
{
    switch (args[0].ToLower())
    {
        case "info": return CmdInfo(args);
        case "export": return CmdExport(args);
        case "selftest": return CmdSelfTest(args);
        case "forge-info": return CmdForgeInfo(args);
        case "forge-export": return CmdForgeExport(args);
        case "list": return CmdList(args);
        case "replace": return CmdReplace(args);
        case "preview": return CmdPreview(args);
        case "patch-all": return CmdPatchAll(args);
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

      list <fajl.forge>
          Ispisuje .data fajlove iz indeksa forgea (bez dekompresije, brzo).

      replace <fajl.forge> <ime-teksture> <slika.png> [izlaz.forge]
          Zamjenjuje teksturu novom slikom. Ako izlaz nije naveden, pise
          pored originala sa nastavkom .patched.

      preview <izlazni-folder>
          Crta sve ikonice tastera kao PNG, da se vidi kako izgledaju prije
          nego se diraju fajlovi igre.

      patch-all <folder-igre> [--apply]
          Prolazi kroz SVE .forge fajlove u folderu igre i zamjenjuje sve
          ikonice tastera odjednom. Bez --apply samo pravi .patched fajlove
          pored originala; sa --apply pravi backup (.bak) i upisuje u igru.

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
    var used = new HashSet<string>();

    int n = 0;
    foreach (var e in textures)
    {
        var tm = new TextureMap(content, e.Offset);
        if (!tm.LooksValid) continue;
        try
        {
            var rgba = tm.DecodeTopMipRgba();
            string outPath = UniquePath(args[2], $"{e.Id}_{e.Name}", used);
            WritePngSafe(outPath, rgba, tm.Width, tm.Height);
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
    var used = new HashSet<string>();

    int n = 0, skipped = 0, failed = 0;
    foreach (var e in textures)
    {
        var tm = new TextureMap(content, e.Offset);
        if (!tm.LooksValid) { skipped++; continue; }
        try
        {
            var rgba = tm.DecodeTopMipRgba();
            WritePngSafe(UniquePath(args[2], $"{e.Id}_{e.Name}", used), rgba, tm.Width, tm.Height);
            n++;
        }
        catch (NotSupportedException) { skipped++; }
        catch (IOException ex)
        {
            // jedan zakljucan fajl ne smije prekinuti cijeli izvoz
            Console.WriteLine($"  nije upisano {e.Name}: {ex.Message}");
            failed++;
        }
    }
    Console.WriteLine($"Izvezeno: {n}, preskoceno (nepodrzan format): {skipped}"
                      + (failed > 0 ? $", neuspjelo: {failed}" : ""));
    return 0;
}


static int CmdList(string[] args)
{
    if (args.Length < 2) { Usage(); return 1; }
    var a = ForgeArchive.Read(args[1]);
    Console.WriteLine($"Forge verzija {a.Version}, zapisa: {a.Entries.Count}");
    foreach (var e in a.Entries)
        Console.WriteLine($"  {e.Index,5}  offset={e.DataOffset,12}  size={e.Size,10:N0}  {e.Name}");
    return 0;
}

static int CmdReplace(string[] args)
{
    if (args.Length < 4) { Usage(); return 1; }
    string forgePath = args[1], texName = args[2], pngPath = args[3];
    string outPath = args.Length > 4 ? args[4] : forgePath + ".patched";

    var rgba = Png.ReadRgba(pngPath, out int pw, out int ph);
    Console.WriteLine($"Slika: {pw}x{ph}");

    var archive = ForgeArchive.Read(forgePath);
    Console.WriteLine($"Forge: {archive.Entries.Count} zapisa");

    var replacements = new Dictionary<int, byte[]>();
    int patched = 0;

    foreach (var e in archive.Entries)
    {
        DataFile df;
        try { df = DataFile.Read(archive.Raw, (int)e.DataOffset, (int)e.DataOffset + e.Size); }
        catch { continue; }

        byte[] content;
        try { content = df.GetContent(); } catch { continue; }

        var hits = DataFile.FindEntries(content, DataEntry.TypeTextureMap)
                           .FindAll(x => x.Name == texName);
        if (hits.Count == 0) continue;

        bool changed = false;
        foreach (var hit in hits)
        {
            var tm = new TextureMap(content, hit.Offset);
            if (!tm.LooksValid) continue;
            if (tm.Width != pw || tm.Height != ph)
            {
                Console.WriteLine($"  preskacem u {e.Name}: tekstura je {tm.Width}x{tm.Height}, slika {pw}x{ph}");
                continue;
            }
            if (tm.Format != TextureMap.FormatRgba)
            {
                Console.WriteLine($"  preskacem u {e.Name}: format {tm.Format} (upis radi samo za RGBA)");
                continue;
            }

            df.PatchBytes(tm.PixelStart, tm.EncodeRgbaWithMips(rgba));
            changed = true;
            patched++;
            Console.WriteLine($"  zamijenjeno u {e.Name} (zapis {e.Index})");
        }

        if (changed) replacements[e.Index] = df.Serialize();
    }

    if (patched == 0)
    {
        Console.WriteLine($"Nisam nasao teksturu '{texName}' - nista nije promijenjeno.");
        return 1;
    }

    archive.Write(outPath, replacements);
    Console.WriteLine($"\nZamijenjeno pojava: {patched}. Snimljeno: {outPath}");
    Console.WriteLine("Originalni fajl nije diran.");
    return 0;
}


/// <summary>
/// Ista tekstura zna se pojaviti vise puta u istom forgeu, pa imena moraju biti
/// jedinstvena - inace se isti fajl pise vise puta zaredom, sto na Windowsu
/// povremeno pukne jer ga antivirus ili Explorer nakratko zakljuca.
/// </summary>
static string UniquePath(string dir, string baseName, HashSet<string> used)
{
    string safe = string.Join("_", baseName.Split(Path.GetInvalidFileNameChars()));
    string candidate = safe;
    int n = 2;
    while (!used.Add(candidate)) candidate = $"{safe}_{n++}";
    return Path.Combine(dir, candidate + ".png");
}

/// <summary>Kratko ceka i pokusa ponovo ako je fajl trenutno zakljucan.</summary>
static void WritePngSafe(string path, byte[] rgba, int w, int h)
{
    for (int attempt = 0; ; attempt++)
    {
        try { Png.WriteRgba(path, rgba, w, h); return; }
        catch (IOException) when (attempt < 5)
        {
            System.Threading.Thread.Sleep(100 * (attempt + 1));
        }
    }
}


static int CmdPreview(string[] args)
{
    if (args.Length < 2) { Usage(); return 1; }
    Directory.CreateDirectory(args[1]);
    foreach (var kv in IconRenderer.IconKeys)
    {
        var px = IconRenderer.Render(32, 32, kv.Value);
        Png.WriteRgba(Path.Combine(args[1], $"{kv.Key}_{kv.Value}.png"), px, 32, 32);
    }
    Console.WriteLine($"Nacrtano {IconRenderer.IconKeys.Count} ikonica u {args[1]}");
    return 0;
}


static int CmdPatchAll(string[] args)
{
    if (args.Length < 2) { Usage(); return 1; }
    string gameDir = args[1];
    bool apply = Array.Exists(args, a => a == "--apply");

    var forges = Directory.GetFiles(gameDir, "*.forge");
    if (forges.Length == 0) { Console.WriteLine($"Nema .forge fajlova u {gameDir}"); return 1; }

    Console.WriteLine($"Forge fajlova: {forges.Length}");
    Console.WriteLine(apply ? "REZIM: upisujem u igru (backup se pravi automatski)"
                            : "REZIM: probni - pisem .patched fajlove pored originala");
    Console.WriteLine();

    // ikonice nacrtamo jednom, iste su za sve forgeove
    var rendered = new Dictionary<string, byte[]>();
    foreach (var kv in IconRenderer.IconKeys)
        rendered[kv.Key] = IconRenderer.Render(32, 32, kv.Value);

    int totalIcons = 0, touchedForges = 0, failed = 0;

    foreach (var forgePath in forges)
    {
        string forgeName = Path.GetFileName(forgePath);
        ForgeArchive archive;
        try { archive = ForgeArchive.Read(forgePath); }
        catch (Exception ex) { Console.WriteLine($"{forgeName}: preskacem ({ex.Message})"); failed++; continue; }

        var replacements = new Dictionary<int, byte[]>();
        int iconsHere = 0, atlasesHere = 0;

        foreach (var e in archive.Entries)
        {
            DataFile df;
            byte[] content;
            try
            {
                df = DataFile.Read(archive.Raw, (int)e.DataOffset, (int)e.DataOffset + e.Size);
                content = df.GetContent();
            }
            catch { continue; }

            bool changed = false;
            foreach (var hit in DataFile.FindEntries(content, DataEntry.TypeTextureMap))
            {
                var tm = new TextureMap(content, hit.Offset);
                if (!tm.LooksValid || tm.Format != TextureMap.FormatRgba) continue;

                // pojedinacne ikonice iz menija
                if (rendered.TryGetValue(hit.Name, out var rgba) && tm.Width == 32 && tm.Height == 32)
                {
                    df.PatchBytes(tm.PixelStart, tm.EncodeRgbaWithMips(rgba));
                    changed = true;
                    iconsHere++;
                    continue;
                }

                // HUD atlas - ono sto se vidi tokom igranja
                if (hit.Name == "HUD_Controls_0_Map")
                {
                    var current = tm.DecodeTopMipRgba();
                    var patchedAtlas = IconRenderer.PatchAtlas(current, tm.Width, tm.Height);
                    df.PatchBytes(tm.PixelStart, tm.EncodeRgbaWithMips(patchedAtlas));
                    changed = true;
                    atlasesHere++;
                }
            }

            if (changed) replacements[e.Index] = df.Serialize();
        }

        if (replacements.Count == 0) { Console.WriteLine($"{forgeName}: nema ikonica"); continue; }
        string what = iconsHere > 0 && atlasesHere > 0 ? $"{iconsHere} ikonica + {atlasesHere} HUD atlas"
                    : atlasesHere > 0 ? $"{atlasesHere} HUD atlas"
                    : $"{iconsHere} ikonica";

        string outPath = apply ? forgePath + ".tmp" : forgePath + ".patched";
        archive.Write(outPath, replacements);

        if (apply)
        {
            string bak = forgePath + ".bak";
            if (!File.Exists(bak)) File.Copy(forgePath, bak);
            File.Delete(forgePath);
            File.Move(outPath, forgePath);
        }

        Console.WriteLine($"{forgeName}: {what} u {replacements.Count} zapisa"
                          + (apply ? "  [upisano, backup .bak]" : $"  -> {Path.GetFileName(outPath)}"));
        totalIcons += iconsHere + atlasesHere;
        touchedForges++;
    }

    Console.WriteLine($"\nUkupno: {totalIcons} ikonica u {touchedForges} forge fajlova"
                      + (failed > 0 ? $", preskoceno {failed}" : ""));
    if (!apply && touchedForges > 0)
        Console.WriteLine("Probni rezim - za stvarni upis dodaj --apply na kraj komande.");
    return 0;
}