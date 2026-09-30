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

    private const string Version = "1.0";
    private const string Author = "Bat0oo";
    private const string GitHubUrl = "https://github.com/Bat0oo/AC2IconPatcher";

    public static int Run(string? gamePathArg = null)
    {
        Console.Title = "AC2 Icon Patcher";
        Header();

        string? game = null;

        if (gamePathArg != null)
        {
            if (GameDetection.IsAssassinsCreed2(gamePathArg)) game = gamePathArg;
            else Console.WriteLine($"'{gamePathArg}' doesn't look like an AC2 install - ignoring it.\n");
        }

        if (game == null)
        {
            game = FindGame();
            if (game != null)
            {
                Console.WriteLine($"Found: {game}");
                Console.Write("Is this the right folder? (yes/no): ");
                if ((Console.ReadLine() ?? "").Trim().ToLower() is not ("yes" or "y" or ""))
                    game = null;
            }
        }

        while (game == null)
        {
            Console.WriteLine("\nEnter the path to your Assassin's Creed 2 folder");
            Console.WriteLine("(the one with DataPC.forge and AssassinsCreedII.exe):");
            Console.WriteLine(@"Example: E:\SteamLibrary\steamapps\common\Assassin's Creed 2");
            Console.Write("> ");
            string input = GameDetection.NormalizePathInput(Console.ReadLine());

            if (input.Length == 0) { Pause(); return 1; }
            if (GameDetection.IsAssassinsCreed2(input)) { game = input; break; }

            Console.WriteLine("That doesn't look like an AC2 install. Leave empty to quit.");
        }

        Console.WriteLine($"\nGame: {game}\n");

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
                        if (LooksPatched(game))
                        {
                            Console.WriteLine("WARNING: the game files are already patched. Installing again");
                            Console.WriteLine("would draw keys over already-modified icons, and the brightness");
                            Console.WriteLine("of the HUD atlas would drift.");
                            Console.WriteLine("Recommended: option 4 (restore original) first, then install.\n");
                            Console.Write("Continue anyway? (yes/no): ");
                            if ((Console.ReadLine() ?? "").Trim().ToLower() is not ("yes" or "y"))
                            { Console.WriteLine("Cancelled."); break; }
                        }
                        Console.WriteLine("This changes the game files. Originals are kept as .bak.");
                        Console.WriteLine("Expect a few minutes and a couple of GB of free space.");
                        Console.WriteLine();
                        Console.WriteLine("NOTE: the icons show the DEFAULT controls. If you have rebound any");
                        Console.WriteLine("keys, those icons will show the wrong key - reading your own bindings");
                        Console.WriteLine("is not supported yet.");
                        Console.Write("\nContinue? (yes/no): ");
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
        Console.WriteLine(new string('=', 68));
        Console.WriteLine("   AC2 ICON PATCHER  v" + Version);
        Console.WriteLine("   Keyboard key icons for Assassin's Creed 2 (PC, 2010)");
        Console.WriteLine(new string('=', 68));
        Console.WriteLine();
        Console.WriteLine("  What it does");
        Console.WriteLine("    Replaces the head / hand / legs symbols in menus and in the");
        Console.WriteLine("    HUD with the actual keys: E, Shift, Space, mouse buttons, WASD.");
        Console.WriteLine();
        Console.WriteLine("  How to use it");
        Console.WriteLine("    Close the game first, then pick 3 to install.");
        Console.WriteLine("    Your original files are backed up as .bak, and option 4");
        Console.WriteLine("    puts them back at any time.");
        Console.WriteLine();
        Console.WriteLine("  Good to know");
        Console.WriteLine("    - Steam's \"Verify integrity of game files\" undoes the mod");
        Console.WriteLine("    - Assumes the default control scheme");
        Console.WriteLine("    - Original AC2 only, not the Ezio Collection remaster");
        Console.WriteLine();
        WriteColored($"  By {Author} - {GitHubUrl}", ConsoleColor.Cyan);
        Console.WriteLine("  Free and open source (GPL). Issues and stars welcome.");
        Console.WriteLine(new string('=', 68));
        Console.WriteLine();
    }

    /// <summary>
    /// A .bak file only means an install happened at some point - the user may have
    /// restored since, which leaves the backups behind. So compare sizes: if every
    /// forge still matches its backup, the game is on original files.
    /// </summary>
    private static bool LooksPatched(string game)
    {
        foreach (var bak in Directory.GetFiles(game, "*.forge.bak"))
        {
            string original = bak[..^4];
            if (!File.Exists(original)) continue;
            if (new FileInfo(original).Length != new FileInfo(bak).Length) return true;
        }
        return false;
    }

    /// <summary>Looks for the game in the usual places, then across every drive.</summary>
    private static string? FindGame()
    {
        var candidates = new List<string>();
        foreach (var root in new[]
        {
            @"C:\Program Files (x86)\Steam\steamapps\common",
            @"C:\Program Files\Steam\steamapps\common",
        }) candidates.Add(root);

        // Steam and Ubisoft Connect both get scanned on every drive - either can
        // live on a library drive other than C:, and there's no way to know which.
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            candidates.Add(Path.Combine(drive.Name, "SteamLibrary", "steamapps", "common"));
            candidates.Add(Path.Combine(drive.Name, "Games"));
            candidates.Add(Path.Combine(drive.Name, "Steam", "steamapps", "common"));
            candidates.Add(Path.Combine(drive.Name, "Program Files (x86)", "Ubisoft", "Ubisoft Game Launcher", "games"));
            candidates.Add(Path.Combine(drive.Name, "Ubisoft", "Ubisoft Game Launcher", "games"));
        }

        foreach (var root in candidates)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in SafeDirs(root))
                if (GameDetection.IsAssassinsCreed2(dir))
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
        var result = patcher.PatchAll(forges, apply, forge =>
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
        });

        int changedForges = result.TouchedForges;
        var took = DateTime.Now - started;
        Console.WriteLine($"\nDone in {took.TotalMinutes:F1} min. Forge files changed: {changedForges}");
        if (apply)
        {
            Console.WriteLine("Originals were saved as .bak next to each file.");
            Console.WriteLine("Start the game and check the controls screen or any button prompt.");
            Console.WriteLine();
            Console.WriteLine(new string('=', 68));
            Console.Write($"  Done! If this helped, drop a star: ");
            WriteColored(GitHubUrl, ConsoleColor.Cyan);
            WriteColored("  It takes two seconds and genuinely helps the project.", ConsoleColor.DarkGray);
            Console.WriteLine("  Found a wrong key or a bug? Open an issue there.");
            Console.WriteLine(new string('=', 68));
        }
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

    private static void WriteColored(string text, ConsoleColor color)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }
}
