using System;
using System.IO;
using System.Linq;
using AC2IconPatcher;

// With no arguments (e.g. double-clicking the exe) we launch the interactive wizard.
if (args.Length == 0)
{
    try { return Wizard.Run(); }
    catch (Exception ex)
    {
        Console.WriteLine("ERROR: " + ex.Message);
        Console.WriteLine("\nPress Enter to exit.");
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
    Console.Error.WriteLine("ERROR: " + ex.Message);
    return 1;
}

static void Usage()
{
    Console.WriteLine("""
    AC2IconPatcher - HUD icon tool for Assassin's Creed 2

      info <file.data>
          Prints blocks, chunks, checksum verification and found textures.

      export <file.data> <output-folder>
          Extracts all textures from a .data file as PNG.

      forge-info <file.forge>
          Scans the whole .forge and prints found textures.

      forge-export <file.forge> <output-folder>
          Extracts all textures from a .forge file as PNG.

      list <file.forge>
          Prints the .data files from the forge's index (no decompression, fast).

      replace <file.forge> <texture-name> <image.png> [output.forge]
          Replaces a texture with a new image. If no output is given, writes
          next to the original with a .patched suffix.

      preview [output-folder]
          Draws all key icons as PNG, so you can see how they look before
          touching the game files. Defaults to an "icons" folder next to
          the exe if no output folder is given.

      patch-all <game-folder> [--apply]
          Goes through ALL .forge files in the game folder and replaces all
          key icons at once. Without --apply it only creates .patched files
          next to the originals; with --apply it makes a backup (.bak) and
          writes into the game.

      selftest <folder-with-data-files>
          Goes through all .data files in a folder, decompresses them and
          verifies every chunk's checksum. This is a test of whether the
          decoder works correctly.
    """);
}

static int CmdInfo(string[] args)
{
    if (args.Length < 2) { Usage(); return 1; }
    var df = DataFile.ReadFile(args[1]);

    Console.WriteLine($"Blocks: {df.Blocks.Count}");
    int ci = 0;
    foreach (var (blk, i) in df.Blocks.Select((b, i) => (b, i)))
    {
        int stored = blk.Chunks.Count(c => c.IsStored);
        Console.WriteLine($"  block {i}: version={blk.Version} compression={blk.CompressionType} " +
                          $"maxChunk={blk.MaxChunkSize} chunks={blk.Chunks.Count} (raw: {stored})");
        ci += blk.Chunks.Count;
    }

    var (ok, bad) = df.VerifyChecksums();
    Console.WriteLine($"Checksums: {ok} valid, {bad} invalid");

    var content = df.GetContent();
    Console.WriteLine($"Decompressed: {content.Length:N0} bytes");

    var textures = DataFile.FindEntries(content, DataEntry.TypeTextureMap);
    Console.WriteLine($"\nTextures ({textures.Count}):");
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
        catch (NotSupportedException ex) { Console.WriteLine($"  skipped {e.Name}: {ex.Message}"); }
    }
    Console.WriteLine($"\nExported: {n}");
    return 0;
}

static int CmdSelfTest(string[] args)
{
    if (args.Length < 2) { Usage(); return 1; }
    var files = Directory.GetFiles(args[1], "*.data").OrderBy(x => x).ToArray();
    if (files.Length == 0) { Console.WriteLine("No .data files in that folder."); return 1; }

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
                              $"{content.Length,12:N0} B  chunks OK: {ok}, bad: {bad}");
            if (good) pass++; else fail++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL {Path.GetFileName(f)}: {ex.Message}");
            fail++;
        }
    }
    Console.WriteLine($"\n{pass} passed, {fail} failed");
    return fail > 0 ? 1 : 0;
}


static int CmdForgeInfo(string[] args)
{
    if (args.Length < 2) { Usage(); return 1; }
    var archive = ForgeArchive.Read(args[1]);
    Console.WriteLine($"Forge version: {archive.Version}, entries: {archive.Entries.Count}");

    int totalTextures = 0;
    foreach (var entry in archive.Entries)
    {
        byte[] content;
        try { content = DataFile.Read(archive.Raw, (int)entry.DataOffset, (int)entry.DataOffset + entry.Size).GetContent(); }
        catch { continue; }

        var textures = DataFile.FindEntries(content, DataEntry.TypeTextureMap);
        if (textures.Count == 0) continue;

        Console.WriteLine($"\n{entry.Name} (entry {entry.Index}), textures ({textures.Count}):");
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
            totalTextures++;
        }
    }
    Console.WriteLine($"\nTotal textures: {totalTextures}");
    return 0;
}

static int CmdForgeExport(string[] args)
{
    if (args.Length < 3) { Usage(); return 1; }
    Directory.CreateDirectory(args[2]);
    var archive = ForgeArchive.Read(args[1]);
    var used = new HashSet<string>();

    int n = 0, skipped = 0, failed = 0;
    foreach (var entry in archive.Entries)
    {
        byte[] content;
        try { content = DataFile.Read(archive.Raw, (int)entry.DataOffset, (int)entry.DataOffset + entry.Size).GetContent(); }
        catch { continue; }

        foreach (var e in DataFile.FindEntries(content, DataEntry.TypeTextureMap))
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
                // one locked file must not abort the whole export
                Console.WriteLine($"  not written {e.Name}: {ex.Message}");
                failed++;
            }
        }
    }
    Console.WriteLine($"Exported: {n}, skipped (unsupported format): {skipped}"
                      + (failed > 0 ? $", failed: {failed}" : ""));
    return 0;
}


static int CmdList(string[] args)
{
    if (args.Length < 2) { Usage(); return 1; }
    var a = ForgeArchive.Read(args[1]);
    Console.WriteLine($"Forge version {a.Version}, entries: {a.Entries.Count}");
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
    Console.WriteLine($"Image: {pw}x{ph}");

    var archive = ForgeArchive.Read(forgePath);
    Console.WriteLine($"Forge: {archive.Entries.Count} entries");

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
                Console.WriteLine($"  skipping in {e.Name}: texture is {tm.Width}x{tm.Height}, image is {pw}x{ph}");
                continue;
            }
            if (tm.Format != TextureMap.FormatRgba)
            {
                Console.WriteLine($"  skipping in {e.Name}: format {tm.Format} (writing only supports RGBA)");
                continue;
            }

            df.PatchBytes(tm.PixelStart, tm.EncodeRgbaWithMips(rgba));
            changed = true;
            patched++;
            Console.WriteLine($"  replaced in {e.Name} (entry {e.Index})");
        }

        if (changed) replacements[e.Index] = df.Serialize();
    }

    if (patched == 0)
    {
        Console.WriteLine($"Could not find texture '{texName}' - nothing was changed.");
        return 1;
    }

    archive.Write(outPath, replacements);
    Console.WriteLine($"\nReplaced occurrences: {patched}. Saved: {outPath}");
    Console.WriteLine("Original file was not touched.");
    return 0;
}


/// <summary>
/// The same texture can appear more than once in the same forge, so names must be
/// unique - otherwise the same file gets written several times in a row, which on
/// Windows occasionally fails because antivirus or Explorer briefly locks it.
/// </summary>
static string UniquePath(string dir, string baseName, HashSet<string> used)
{
    string safe = string.Join("_", baseName.Split(Path.GetInvalidFileNameChars()));
    string candidate = safe;
    int n = 2;
    while (!used.Add(candidate)) candidate = $"{safe}_{n++}";
    return Path.Combine(dir, candidate + ".png");
}

/// <summary>Waits briefly and retries if the file is currently locked.</summary>
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
    string outDir = args.Length >= 2 ? args[1] : Path.Combine(AppContext.BaseDirectory, Wizard.PreviewDirName);
    Directory.CreateDirectory(outDir);
    foreach (var kv in IconRenderer.IconKeys)
    {
        var px = IconRenderer.Render(32, 32, kv.Value);
        Png.WriteRgba(Path.Combine(outDir, $"{kv.Key}_{kv.Value}.png"), px, 32, 32);
    }
    Console.WriteLine($"Drew {IconRenderer.IconKeys.Count} icons into {outDir}");
    return 0;
}


static int CmdPatchAll(string[] args)
{
    if (args.Length < 2) { Usage(); return 1; }
    string gameDir = args[1];
    bool apply = Array.Exists(args, a => a == "--apply");

    var forges = Directory.GetFiles(gameDir, "*.forge");
    if (forges.Length == 0) { Console.WriteLine($"No .forge files in {gameDir}"); return 1; }

    Console.WriteLine($"Forge files: {forges.Length}");
    Console.WriteLine(apply ? "MODE: writing into the game (backup is made automatically)"
                            : "MODE: dry run - writing .patched files next to the originals");
    Console.WriteLine();

    var patcher = new IconPatcher();
    var result = patcher.PatchAll(gameDir, apply);

    foreach (var forge in result.Forges)
    {
        switch (forge.Outcome)
        {
            case PatchOutcome.Skipped:
                Console.WriteLine($"{forge.ForgeName}: skipping ({forge.Error})");
                break;
            case PatchOutcome.NothingToPatch:
                Console.WriteLine($"{forge.ForgeName}: no icons");
                break;
            case PatchOutcome.Patched:
                string what = forge.IconsPatched > 0 && forge.AtlasesPatched > 0
                    ? $"{forge.IconsPatched} icons + {forge.AtlasesPatched} HUD atlas"
                    : forge.AtlasesPatched > 0 ? $"{forge.AtlasesPatched} HUD atlas"
                    : $"{forge.IconsPatched} icons";
                Console.WriteLine($"{forge.ForgeName}: {what}"
                                  + (apply ? "  [written, backup .bak]" : $"  -> {Path.GetFileName(forge.OutputPath)}"));
                break;
        }
    }

    Console.WriteLine($"\nTotal: {result.TotalIconsPatched} icons in {result.TouchedForges} forge files"
                      + (result.FailedForges > 0 ? $", skipped {result.FailedForges}" : ""));
    if (!apply && result.TouchedForges > 0)
        Console.WriteLine("Dry run - add --apply at the end of the command for the real write.");
    return 0;
}
