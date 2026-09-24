using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AC2IconPatcher;

/// <summary>
/// Interactive menu - what runs when the exe is opened by double-click, with
/// no arguments. The commands in Program.cs still work for terminal users.
/// </summary>
public static class Wizard
{
    /// <summary>Default output folder for drawn-icon previews; kept in sync with the CLI's preview command.</summary>
    public const string PreviewDirName = "icons";

    public static int Run()
    {
        Console.Title = "AC2 Icon Patcher";
        Header();

        string? game = FindGame();
        if (game == null)
        {
            Console.WriteLine("Could not find an Assassin's Creed 2 install automatically.");
            Console.WriteLine("Enter the path to the game folder (the one with DataPC.forge):");
            Console.Write("> ");
            game = (Console.ReadLine() ?? "").Trim().Trim('"');
            if (!File.Exists(Path.Combine(game, "DataPC.forge")))
            {
                Console.WriteLine("\nNo DataPC.forge there. Stopping.");
                Pause();
                return 1;
            }
        }

        Console.WriteLine($"Game: {game}\n");

        while (true)
        {
            Console.WriteLine("  1  Preview how the icons look (creates PNGs, does not touch the game)");
            Console.WriteLine("  2  Dry run (shows what would change, does not touch the game)");
            Console.WriteLine("  3  Install keyboard icons  [changes the game, makes a backup]");
            Console.WriteLine("  4  Restore original from backup");
            Console.WriteLine("  5  Exit");
            Console.Write("\nChoice: ");

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
                            Console.WriteLine("WARNING: .bak files already exist in the game folder, which means");
                            Console.WriteLine("the mod is already installed. Installing again would draw keys");
                            Console.WriteLine("over already-modified icons and needlessly bloat the files.");
                            Console.WriteLine("Recommended: option 4 (restore original) first, then install.\n");
                            Console.Write("Continue anyway? (yes/no): ");
                            if ((Console.ReadLine() ?? "").Trim().ToLower() is not ("yes" or "y"))
                            { Console.WriteLine("Cancelled."); break; }
                        }
                        Console.WriteLine("This changes the game files. Originals are kept as .bak.");
                        Console.WriteLine("Expect a few minutes and a couple of GB of free space.");
                        Console.Write("Continue? (yes/no): ");
                        if ((Console.ReadLine() ?? "").Trim().ToLower() is "yes" or "y")
                            Patch(game, apply: true);
                        else Console.WriteLine("Cancelled.");
                        break;
                    case "4": Restore(game); break;
                    case "5": return 0;
                    default: Console.WriteLine("Unknown choice."); break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nERROR: {ex.Message}");
            }
            Console.WriteLine("\n" + new string('-', 60) + "\n");
        }
    }

    private static void Header()
    {
        Console.WriteLine(new string('=', 60));
        Console.WriteLine("  AC2 Icon Patcher");
        Console.WriteLine("  Replaces HUD icons in Assassin's Creed 2 with keyboard keys");
        Console.WriteLine(new string('=', 60));
        Console.WriteLine();
    }

    /// <summary>Looks for the game in the usual places, then across every drive.</summary>
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
        string dir = Path.Combine(AppContext.BaseDirectory, PreviewDirName);
        Directory.CreateDirectory(dir);
        foreach (var kv in IconRenderer.IconKeys)
            Png.WriteRgba(Path.Combine(dir, $"{kv.Key}_{kv.Value}.png"),
                          IconRenderer.Render(32, 32, kv.Value), 32, 32);
        Console.WriteLine($"Drew {IconRenderer.IconKeys.Count} icons.");
        Console.WriteLine($"Folder: {dir}");
    }

    private static void Patch(string game, bool apply)
    {
        var forges = Directory.GetFiles(game, "*.forge");
        Console.WriteLine($"Forge files: {forges.Length}");
        Console.WriteLine(apply ? "Installing...\n" : "Dry run, nothing is being changed...\n");

        if (apply)
        {
            try
            {
                long needed = forges.Sum(f => new FileInfo(f).Length);
                long free = new DriveInfo(Path.GetPathRoot(game)!).AvailableFreeSpace;
                if (free < needed * 1.2)
                {
                    Console.WriteLine($"Free space is low: need about {needed / 1024 / 1024 / 1024.0:F1} GB " +
                                      $"for the backup, and {free / 1024 / 1024 / 1024.0:F1} GB is free.");
                    Console.Write("Continue anyway? (yes/no): ");
                    if ((Console.ReadLine() ?? "").Trim().ToLower() is not ("yes" or "y"))
                    { Console.WriteLine("Cancelled."); return; }
                }
            }
            catch { /* if we can't check free space, just continue */ }
        }

        var started = DateTime.Now;
        var progress = new SyncProgress<PatchProgress>(p => Console.Write($"[{p.Index}/{p.Total}] {p.ForgeName} ... "));
        var patcher = new IconPatcher(progress);
        var result = patcher.PatchAll(game, apply);

        foreach (var forge in result.Forges)
        {
            switch (forge.Outcome)
            {
                case PatchOutcome.Skipped:
                    Console.WriteLine("skipping");
                    break;
                case PatchOutcome.NothingToPatch:
                    Console.WriteLine("no icons");
                    break;
                case PatchOutcome.Patched:
                    Console.WriteLine(forge.IconsPatched > 0 && forge.AtlasesPatched > 0
                        ? $"{forge.IconsPatched} icons + HUD atlas"
                        : forge.AtlasesPatched > 0 ? "HUD atlas" : $"{forge.IconsPatched} icons");
                    break;
            }
        }

        int changedForges = result.TouchedForges;
        var took = DateTime.Now - started;
        Console.WriteLine($"\nDone in {took.TotalMinutes:F1} min. Forge files changed: {changedForges}");
        if (apply) Console.WriteLine("Originals were saved as .bak next to each file.");
        else
        {
            var temps = Directory.GetFiles(game, "*.forge.patched");
            if (temps.Length > 0)
            {
                Console.Write($"The dry run created {temps.Length} .patched files. Delete them? (yes/no): ");
                if ((Console.ReadLine() ?? "").Trim().ToLower() is "yes" or "y")
                {
                    foreach (var t in temps) File.Delete(t);
                    Console.WriteLine("Deleted.");
                }
            }
        }
    }

    private static void Restore(string game)
    {
        var backups = Directory.GetFiles(game, "*.forge.bak");
        if (backups.Length == 0) { Console.WriteLine("No backups found."); return; }

        Console.WriteLine($"Backups found: {backups.Length}");
        foreach (var bak in backups)
        {
            string original = bak[..^4];
            File.Copy(bak, original, overwrite: true);
            Console.WriteLine($"  restored {Path.GetFileName(original)}");
        }
        long totalBytes = backups.Sum(b => new FileInfo(b).Length);
        Console.WriteLine($"\nEverything restored to original.");
        Console.WriteLine($"Backups take up {totalBytes / 1024.0 / 1024 / 1024:F1} GB.");
        Console.Write("Delete them? (yes/no): ");
        if ((Console.ReadLine() ?? "").Trim().ToLower() is "yes" or "y")
        {
            foreach (var bak in backups) File.Delete(bak);
            Console.WriteLine("Backups deleted.");
        }
        else Console.WriteLine("Backups were left in the game folder.");
    }

    private static void Pause()
    {
        Console.WriteLine("\nPress Enter to exit.");
        Console.ReadLine();
    }
}
