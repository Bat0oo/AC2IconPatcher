using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AC2IconPatcher;

/// <summary>
/// Interaktivni meni - ono sto se pokrene kad se exe otvori duplim klikom,
/// bez ijednog argumenta. Komande iz Program.cs i dalje rade za one koji
/// vole terminal.
/// </summary>
public static class Wizard
{
    public static int Run()
    {
        Console.Title = "AC2 Icon Patcher";
        Header();

        string? game = FindGame();
        if (game == null)
        {
            Console.WriteLine("Ne mogu sam pronaci instalaciju Assassin's Creed 2.");
            Console.WriteLine("Upisi putanju do foldera igre (onaj gdje je DataPC.forge):");
            Console.Write("> ");
            game = (Console.ReadLine() ?? "").Trim().Trim('"');
            if (!File.Exists(Path.Combine(game, "DataPC.forge")))
            {
                Console.WriteLine("\nTu nema DataPC.forge. Prekidam.");
                Pause();
                return 1;
            }
        }

        Console.WriteLine($"Igra: {game}\n");

        while (true)
        {
            Console.WriteLine("  1  Pogledaj kako izgledaju ikonice (pravi PNG-ove, ne dira igru)");
            Console.WriteLine("  2  Probni prolaz (pokaze sta bi se promijenilo, ne dira igru)");
            Console.WriteLine("  3  Instaliraj ikonice tastature  [mijenja igru, pravi backup]");
            Console.WriteLine("  4  Vrati original iz backupa");
            Console.WriteLine("  5  Izlaz");
            Console.Write("\nIzbor: ");

            string choice = (Console.ReadLine() ?? "").Trim();
            Console.WriteLine();

            try
            {
                switch (choice)
                {
                    case "1": Preview(); break;
                    case "2": Patch(game, apply: false); break;
                    case "3":
                        if (Directory.GetFiles(game, "*.forge.bak").Length > 0)
                        {
                            Console.WriteLine("UPOZORENJE: u folderu igre vec postoje .bak fajlovi, sto znaci");
                            Console.WriteLine("da je mod vec instaliran. Ponovna instalacija bi crtala tipke");
                            Console.WriteLine("preko vec izmijenjenih ikonica i bespotrebno naduvala fajlove.");
                            Console.WriteLine("Preporuka: prvo opcija 4 (vrati original), pa onda instaliraj.\n");
                            Console.Write("Svejedno nastaviti? (da/ne): ");
                            if ((Console.ReadLine() ?? "").Trim().ToLower() is not ("da" or "d" or "yes" or "y"))
                            { Console.WriteLine("Otkazano."); break; }
                        }
                        Console.WriteLine("Ovo mijenja fajlove igre. Originali se cuvaju kao .bak.");
                        Console.WriteLine("Racunaj na nekoliko minuta i par GB slobodnog prostora.");
                        Console.Write("Nastaviti? (da/ne): ");
                        if ((Console.ReadLine() ?? "").Trim().ToLower() is "da" or "d" or "yes" or "y")
                            Patch(game, apply: true);
                        else Console.WriteLine("Otkazano.");
                        break;
                    case "4": Restore(game); break;
                    case "5": return 0;
                    default: Console.WriteLine("Nepoznat izbor."); break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nGRESKA: {ex.Message}");
            }
            Console.WriteLine("\n" + new string('-', 60) + "\n");
        }
    }

    private static void Header()
    {
        Console.WriteLine(new string('=', 60));
        Console.WriteLine("  AC2 Icon Patcher");
        Console.WriteLine("  Zamjenjuje HUD ikonice u Assassin's Creed 2 tipkama tastature");
        Console.WriteLine(new string('=', 60));
        Console.WriteLine();
    }

    /// <summary>Trazi igru na uobicajenim mjestima, pa po svim diskovima.</summary>
    private static string? FindGame()
    {
        var candidates = new List<string>();
        foreach (var root in new[]
        {
            @"C:\Program Files (x86)\Steam\steamapps\common",
            @"C:\Program Files\Steam\steamapps\common",
            @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games",
        }) candidates.Add(root);

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            candidates.Add(Path.Combine(drive.Name, "SteamLibrary", "steamapps", "common"));
            candidates.Add(Path.Combine(drive.Name, "Games"));
            candidates.Add(Path.Combine(drive.Name, "Steam", "steamapps", "common"));
        }

        foreach (var root in candidates)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in SafeDirs(root))
                if (File.Exists(Path.Combine(dir, "DataPC.forge")))
                    return dir;
        }
        return null;
    }

    private static IEnumerable<string> SafeDirs(string root)
    {
        try { return Directory.GetDirectories(root); }
        catch { return []; }
    }

    private static void Preview()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "ikone");
        Directory.CreateDirectory(dir);
        foreach (var kv in IconRenderer.IconKeys)
            Png.WriteRgba(Path.Combine(dir, $"{kv.Key}_{kv.Value}.png"),
                          IconRenderer.Render(32, 32, kv.Value), 32, 32);
        Console.WriteLine($"Nacrtano {IconRenderer.IconKeys.Count} ikonica.");
        Console.WriteLine($"Folder: {dir}");
    }

    private static void Patch(string game, bool apply)
    {
        var forges = Directory.GetFiles(game, "*.forge");
        Console.WriteLine($"Forge fajlova: {forges.Length}");
        Console.WriteLine(apply ? "Instaliram...\n" : "Probni prolaz, nista se ne mijenja...\n");

        var rendered = new Dictionary<string, byte[]>();
        foreach (var kv in IconRenderer.IconKeys)
            rendered[kv.Key] = IconRenderer.Render(32, 32, kv.Value);

        if (apply)
        {
            try
            {
                long needed = forges.Sum(f => new FileInfo(f).Length);
                long free = new DriveInfo(Path.GetPathRoot(game)!).AvailableFreeSpace;
                if (free < needed * 1.2)
                {
                    Console.WriteLine($"Malo je slobodnog prostora: treba oko {needed / 1024 / 1024 / 1024.0:F1} GB " +
                                      $"za backup, a slobodno je {free / 1024 / 1024 / 1024.0:F1} GB.");
                    Console.Write("Svejedno nastaviti? (da/ne): ");
                    if ((Console.ReadLine() ?? "").Trim().ToLower() is not ("da" or "d" or "yes" or "y"))
                    { Console.WriteLine("Otkazano."); return; }
                }
            }
            catch { /* ako ne mozemo provjeriti prostor, samo nastavljamo */ }
        }

        int done = 0, changedForges = 0;
        var started = DateTime.Now;

        foreach (var forgePath in forges)
        {
            done++;
            string name = Path.GetFileName(forgePath);
            Console.Write($"[{done}/{forges.Length}] {name} ... ");

            ForgeArchive archive;
            try { archive = ForgeArchive.Read(forgePath); }
            catch { Console.WriteLine("preskacem"); continue; }

            var replacements = new Dictionary<int, byte[]>();
            int icons = 0, atlases = 0;

            foreach (var e in archive.Entries)
            {
                DataFile df; byte[] content;
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

                    if (rendered.TryGetValue(hit.Name, out var rgba) && tm.Width == 32 && tm.Height == 32)
                    {
                        df.PatchBytes(tm.PixelStart, tm.EncodeRgbaWithMips(rgba));
                        changed = true; icons++;
                    }
                    else if (hit.Name == "HUD_Controls_0_Map")
                    {
                        var patched = IconRenderer.PatchAtlas(tm.DecodeTopMipRgba(), tm.Width, tm.Height);
                        df.PatchBytes(tm.PixelStart, tm.EncodeRgbaWithMips(patched));
                        changed = true; atlases++;
                    }
                }
                if (changed) replacements[e.Index] = df.Serialize();
            }

            if (replacements.Count == 0) { Console.WriteLine("nema ikonica"); continue; }

            if (apply)
            {
                string bak = forgePath + ".bak";
                if (!File.Exists(bak)) File.Copy(forgePath, bak);
                string tmp = forgePath + ".tmp";
                archive.Write(tmp, replacements);
                File.Delete(forgePath);
                File.Move(tmp, forgePath);
            }
            else archive.Write(forgePath + ".patched", replacements);

            Console.WriteLine(icons > 0 && atlases > 0 ? $"{icons} ikonica + HUD atlas"
                            : atlases > 0 ? "HUD atlas" : $"{icons} ikonica");
            changedForges++;
        }

        var took = DateTime.Now - started;
        Console.WriteLine($"\nGotovo za {took.TotalMinutes:F1} min. Izmijenjeno forge fajlova: {changedForges}");
        if (apply) Console.WriteLine("Originali su sacuvani kao .bak pored svakog fajla.");
        else
        {
            var temps = Directory.GetFiles(game, "*.forge.patched");
            if (temps.Length > 0)
            {
                Console.Write($"Probni prolaz je napravio {temps.Length} .patched fajlova. Obrisati ih? (da/ne): ");
                if ((Console.ReadLine() ?? "").Trim().ToLower() is "da" or "d" or "yes" or "y")
                {
                    foreach (var t in temps) File.Delete(t);
                    Console.WriteLine("Obrisano.");
                }
            }
        }
    }

    private static void Restore(string game)
    {
        var backups = Directory.GetFiles(game, "*.forge.bak");
        if (backups.Length == 0) { Console.WriteLine("Nema nijednog backupa."); return; }

        Console.WriteLine($"Pronadjeno backupa: {backups.Length}");
        foreach (var bak in backups)
        {
            string original = bak[..^4];
            File.Copy(bak, original, overwrite: true);
            Console.WriteLine($"  vracen {Path.GetFileName(original)}");
        }
        long totalBytes = backups.Sum(b => new FileInfo(b).Length);
        Console.WriteLine($"\nSve vraceno na original.");
        Console.WriteLine($"Backupi zauzimaju {totalBytes / 1024.0 / 1024 / 1024:F1} GB.");
        Console.Write("Obrisati ih? (da/ne): ");
        if ((Console.ReadLine() ?? "").Trim().ToLower() is "da" or "d" or "yes" or "y")
        {
            foreach (var bak in backups) File.Delete(bak);
            Console.WriteLine("Backupi obrisani.");
        }
        else Console.WriteLine("Backupi su ostali u folderu igre.");
    }

    private static void Pause()
    {
        Console.WriteLine("\nPritisni Enter za izlaz.");
        Console.ReadLine();
    }
}
